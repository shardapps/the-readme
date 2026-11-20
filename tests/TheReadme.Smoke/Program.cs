using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TheReadme;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "the-readme-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var watchdog = timeout.Token.Register(() => dispatcher.BeginInvokeShutdown(DispatcherPriority.Send));
            string? screenshots = args is [var directory] ? Path.GetFullPath(directory) : null;
            int exit = 1;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await Run(temporary, screenshots, timeout.Token);
                    exit = 0;
                    Console.WriteLine("Format, rendering, editing, saving, responsive layout, settings and cancellation checks passed.");
                }
                catch (Exception exception) { Console.Error.WriteLine(exception); }
                finally { app.Shutdown(exit); }
            });
            app.Run();
            return exit;
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    private static async Task Run(string temporary, string? screenshots, CancellationToken token)
    {
        string path = Path.Combine(temporary, "README");
        string fixture = """
            # The Test & Examples

            Styled **needle** crossing _format boundaries_.

            ## Usage

            | Key | Action |
            | --- | --- |
            | Ctrl+O | Open |

            - [x] Read
            - [ ] Search

            5. Five
            6. Six

            [Jump](#usage) [External](https://example.org) ![Local illustration](cover.png) ![Remote illustration](https://example.test/x.png)

            <script>plain text only</script>
            """;
        File.WriteAllText(path, fixture);
        File.WriteAllText(Path.Combine(temporary, "UTF16.txt"), "A line of Unicode ☼", Encoding.Unicode);
        File.WriteAllBytes(Path.Combine(temporary, "binary.md"), [65, 0, 66]);
        File.WriteAllBytes(Path.Combine(temporary, "bad-encoding.md"), [0xff, 0xff, 0xff]);
        using (var large = File.Create(Path.Combine(temporary, "too-big.md"))) large.SetLength(ReaderFile.MaxBytes + 1);

        var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 128, 64, 96, 255 }, 4);
        var image = new PngBitmapEncoder();
        image.Frames.Add(BitmapFrame.Create(pixel));
        using (var output = File.Create(Path.Combine(temporary, "cover.png"))) image.Save(output);

        var file = await ReaderFile.OpenAsync(path, token);
        Require(!file.IsPlainText && file.Text == fixture, "Extensionless Markdown");
        Require((await ReaderFile.OpenAsync(Path.Combine(temporary, "UTF16.txt"), token)).Text.Contains('☼'), "UTF-16 BOM");
        await MustReject(Path.Combine(temporary, "binary.md"), token);
        await MustReject(Path.Combine(temporary, "bad-encoding.md"), token);
        await MustReject(Path.Combine(temporary, "too-big.md"), token);
        await MustReject(Path.Combine(temporary, "sample.exe"), token);
        string pythonPath = await CheckFormatsAndSaves(temporary, token);
        Require(DocumentLinks.LocalPath(path, "folder/next.md#anchor") == Path.Combine(temporary, "folder", "next.md"), "Adjacent document");
        Require(DocumentLinks.LocalPath(path, "%2e%2e/outside.md") is null, "Encoded directory traversal");
        Require(DocumentLinks.LocalPath(path, "cover.png:alternate") is null, "Alternate stream");
        Require(DocumentLinks.LocalPath(path, "//example.com/a.md") is null, "Protocol-relative destination");
        Require(!DocumentLinks.TryExternal("file:///C:/Windows/system.ini", out _), "Disallowed file URL");
        Require(!DocumentLinks.TryExternal("javascript:alert(1)", out _), "Disallowed active URL");
        Require(DocumentLinks.TryExternal("https://example.test", out _), "HTTPS destination");
        Require(WindowsIntegration.SamePath("\"C:\\Apps\\The Readme\\\"", "c:\\apps\\the readme"), "PATH casing and quoting");

        var prefs = new ReaderPreferences(Path.Combine(temporary, "prefs.json")) { Theme = "Lilac" };
        var window = new MainWindow(null, prefs)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000,
            ShowInTaskbar = false, ShowActivated = false
        };
        window.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (screenshots is not null) Capture(window, screenshots, "welcome.png");

        var parse = MarkdownView.Parse(file.Text);
        var images = await Task.Run(() => MarkdownView.LoadImages(parse, file, token), token);
        Require(images.Count == 1 && images["cover.png"].IsFrozen, "Local frozen bitmap, remote image omitted");
        var view = new MarkdownView(window, file, images, _ => { });
        FlowDocument document = view.Render(parse);
        Require(view.Outline.Count == 2 && view.Outline[1].Anchor == "usage", "GitHub headings and outline");
        Require(document.Blocks.OfType<Table>().Single().RowGroups[0].Rows.Count == 2, "Markdown table");
        Require(document.Blocks.OfType<List>().Any(list => list.StartIndex == 5), "Ordered list start");
        var search = new DocumentSearch(document);
        string query = "needle crossing format";
        int start = search.Text.IndexOf(query, StringComparison.Ordinal);
        Require(start >= 0, "Search text across inline styling");
        var match = new TextRange(search.PointerAt(start, false)!, search.PointerAt(start + query.Length, true)!);
        Require(match.Text == query, "Search indexes map to rendered selection");
        Require(search.Text.Contains("plain text only"), "HTML is rendered as text");

        string largePath = Path.Combine(temporary, "many.md");
        File.WriteAllText(largePath, string.Concat(Enumerable.Repeat("### Heading\n\n**Text**\n\n", 10000)));
        Task obsolete = window.OpenFile(largePath);
        Task latest = window.OpenFile(path);
        await Task.WhenAll(obsolete, latest);
        Require(prefs.Recent.SequenceEqual([path]), "Superseded open cannot replace latest file");
        Require(window.Title.StartsWith("README", StringComparison.Ordinal), "Latest document displayed");

        var crowded = new ReaderFile(largePath, File.ReadAllText(largePath), FileFormats.Markdown, 0);
        var bounded = new MarkdownView(window, crowded, new Dictionary<string, ImageSource>(), _ => { });
        var boundedDocument = bounded.Render(MarkdownView.Parse(crowded.Text));
        Require(bounded.Outline.Count < 10000 && ((Paragraph)boundedDocument.Blocks.LastBlock).Inlines.OfType<Run>().Single().Text.Contains("Preview limit"), "Rendering budget");

        var dark = new MainWindow(null, new ReaderPreferences(Path.Combine(temporary, "dark.json")) { Theme = "Midnight" })
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000,
            ShowInTaskbar = false, ShowActivated = false
        };
        if (screenshots is not null)
        {
            dark.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var welcome = (ScrollViewer)dark.FindName("Welcome");
            Require(ClickButton(welcome, "Try a sample →"), "Sample button exists");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(200, token);
            Capture(dark, screenshots, "midnight.png");
        }
        dark.Close();

        var preview = (FlowDocumentScrollViewer)window.FindName("Preview");
        Require(preview.Document is not null, "Reading surface loaded");
        var source = (Button)window.FindName("SourceButton");
        source.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(((TextBox)window.FindName("Source")).Text == fixture, "Source mode round-trips Markdown");
        ((Button)window.FindName("ReadButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((Button)window.FindName("FindButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((TextBox)window.FindName("FindBox")).Text = "needle";
        await Task.Delay(250, token);
        Require(((TextBlock)window.FindName("FindCount")).Text == "1 of 1", "Find UI matches text");
        Require(preview.Selection?.Text == "needle", "Find UI selects visible text");

        await CheckWorkspace(window, path, fixture, pythonPath, screenshots, token);

        if (screenshots is not null)
        {
            ChooseTheme(window, "Lilac");
            ((ScrollViewer)window.FindName("Welcome")).Visibility = Visibility.Visible;
            Require(ClickButton((ScrollViewer)window.FindName("Welcome"), "Try a sample →"), "Sample button exists");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(200, token);
            Capture(window, screenshots, "lilac.png");
        }
        window.Close();
    }

    private static async Task<string> CheckFormatsAndSaves(string temporary, CancellationToken token)
    {
        (string Name, string Label)[] sourceFiles =
        [
            ("example.py", "Python"), ("example.c", "C"), ("example.cpp", "C++"), ("Example.java", "Java"),
            ("logic.hdl", "HDL"), ("logic.sv", "Verilog"), ("logic.vhd", "VHDL"), ("script.ps1", "PowerShell"),
            ("app.tsx", "TypeScript"), ("app.mjs", "JavaScript"), ("app.rs", "Rust"), ("app.go", "Go"),
            ("app.swift", "Swift"), ("app.kt", "Kotlin"), ("app.cs", "C#"), ("app.rb", "Ruby"),
            ("app.php", "PHP"), ("app.dart", "Dart"), ("app.lua", "Lua"), ("app.fsx", "F#"),
            ("data.json", "JSON"), ("data.csv", "Delimited text"), ("data.tsv", "Delimited text"), ("data.xml", "XML"),
            ("settings.yml", "YAML"), ("settings.toml", "TOML"), (".editorconfig", "Configuration"),
            ("schema.sql", "SQL"), ("index.html", "HTML"), ("style.css", "Stylesheet"), ("readme.rst", "Text markup"),
            ("output.log", "Log"), ("change.patch", "Diff"), ("Dockerfile", "Build file"), ("Makefile", "Build file"),
            ("CMakeLists.txt", "Build file"), ("script.sh", "Shell"), ("notes.custom", "Text file")
        ];
        const string content = "first line\r\nsecond line\nthird line\rfourth line";
        foreach (var (name, label) in sourceFiles)
        {
            string path = Path.Combine(temporary, name);
            File.WriteAllText(path, content);
            var item = await ReaderFile.OpenAsync(path, token);
            Require(item.Format.Kind == FileKind.Source && item.Format.Label == label && item.Text == content, $"Read-only detection for {name}");
        }
        foreach (string suffix in new[] { ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".txt", ".text" })
        {
            string path = Path.Combine(temporary, "Editable" + suffix);
            File.WriteAllText(path, content);
            Require((await ReaderFile.OpenAsync(path, token)).Format.CanEdit, $"Editable detection for {suffix}");
        }

        string unknownBinary = Path.Combine(temporary, "hidden.unknown");
        await File.WriteAllBytesAsync(unknownBinary, [70, 0, 50, 25, 50], token);
        await MustReject(unknownBinary, token);
        string utf16 = Path.Combine(temporary, "UTF16.txt");
        ReaderFile original = await ReaderFile.OpenAsync(utf16, token);
        var saved = await original.SaveAsync(utf16, "A line of Unicode ☼\r\nMore text", overwrite: false);
        Require(File.ReadAllBytes(utf16).AsSpan().StartsWith(Encoding.Unicode.Preamble) && saved.Text.Contains('\u263c'), "Save preserves Unicode encoding and BOM");
        File.WriteAllText(utf16, "External change");
        try
        {
            await saved.SaveAsync(utf16, "Pending edit", overwrite: false);
            throw new InvalidOperationException("Unexpected overwrite of external edits.");
        }
        catch (IOException) { }
        Require(File.ReadAllText(utf16) == "External change", "Save protects changed files");
        string converted = Path.Combine(temporary, "Converted.md");
        var copy = await saved.SaveAsync(converted, "A Markdown copy", overwrite: false);
        Require(copy.Format.Kind == FileKind.Markdown && File.Exists(converted), "Save As converts text to Markdown");
        Require(!Directory.EnumerateFiles(temporary, ".readme-*.tmp").Any(), "Save removes temporary files after success and failure");
        Require(MarkdownEdits.OffsetOfLine(content, 4) == content.IndexOf("fourth", StringComparison.Ordinal), "Logical line navigation");
        Require(MarkdownEdits.Heading("## Small", 4, 0, 1).Text == "# Small", "Heading format changes sizing");
        Require(MarkdownEdits.Inline("**word**", 2, 4, "*", "").Text == "*word*", "Italic composes with existing bold");
        Require(MarkdownEdits.Inline("***word***", 3, 4, "*", "").Text == "word", "Italic toggles independently in combined emphasis");
        Require(MarkdownEdits.Inline("**word**", 0, 8, "*", "").Text == "***word***", "Applying italic to an entire bold selection keeps bold");
        return Path.Combine(temporary, "example.py");
    }

    private static async Task CheckWorkspace(MainWindow window, string path, string fixture, string pythonPath, string? screenshots, CancellationToken token)
    {
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var preview = Control<FlowDocumentScrollViewer>("Preview");
        var editor = Control<TextBox>("Source");
        var surface = Control<Grid>("DocumentSurface");
        async Task Layout()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
        }

        Click("CloseFindButton");
        // The hosted desktop can constrain the outer window. Size its document
        // viewport directly to exercise both layout ranges on either runner.
        surface.Width = 620;
        await Layout();
        double narrowWidth = preview.ActualWidth;
        surface.Width = 1140;
        await Layout();
        Require(preview.ActualWidth > narrowWidth + 450,
            $"Reading view grows with its viewport ({narrowWidth:0} to {preview.ActualWidth:0} DIPs)");
        surface.ClearValue(FrameworkElement.WidthProperty);
        await Layout();
        double sideWidth = preview.ActualWidth;
        Click("RailButton");
        await Layout();
        Require(preview.ActualWidth > sideWidth + 180, "Reading view expands with sidebar hidden");
        Click("RailButton");
        window.WindowState = WindowState.Maximized;
        await Layout();
        Require(preview.ActualWidth > 100 && preview.ActualWidth >= surface.ActualWidth - preview.Margin.Left - preview.Margin.Right - 2,
            "Maximized reading layout uses the available width");
        window.WindowState = WindowState.Normal;
        await Layout();

        Click("SettingsButton");
        window.UpdateLayout();
        var settings = Control<Border>("SettingsPane");
        Require(settings.IsVisible && Control<WrapPanel>("ThemeButtons").Children.Count >= 7, "Settings contains six or more explicit themes");
        window.DismissSettingsAt(settings.TranslatePoint(new Point(10, 10), window));
        Require(settings.IsVisible, "An inside click keeps Settings open");
        window.DismissSettingsAt(new Point(8, 160));
        Require(!settings.IsVisible, "An outside click dismisses Settings");
        Click("SettingsButton");
        ChooseTheme(window, "Ocean");
        window.DismissSettingsAt(new Point(8, 160));

        surface.Width = 1020;
        Click("SplitButton");
        await Layout();
        Require(preview.IsVisible && editor.IsVisible && !editor.IsReadOnly && Grid.GetColumn(editor) == 0, "Wide split view");
        surface.Width = 710;
        await Layout();
        Require(Grid.GetRow(editor) == 0 && Grid.GetColumn(editor) == 2, "Narrow split stacks editing and preview");
        surface.ClearValue(FrameworkElement.WidthProperty);
        await Layout();
        double normalFont = editor.FontSize;
        Click("ZoomInButton");
        Require(preview.Zoom == 110 && editor.FontSize > normalFont, "Zoom increases both panes");
        Click("ZoomLabel");
        Require(preview.Zoom == 100 && editor.FontSize == normalFont, "Zoom reset restores both panes");

        int selected = editor.Text.IndexOf("Styled", StringComparison.Ordinal);
        editor.Select(selected, "Styled".Length);
        Click("BoldButton");
        Require(editor.Text.Contains("**Styled**") && window.Title.Contains(" *"), "Bold edits Markdown and marks the draft");
        Click("BoldButton");
        Require(editor.Text == fixture, "Bold is reversible");
        editor.AppendText("\n\n## Live alpha heading");
        await WaitUntil(() => new DocumentSearch(preview.Document).Text.Contains("Live alpha heading"), token);
        Require(window.Title.Contains(" *"), "Live edit stays unsaved until saved");
        window.ConfirmUnsavedForTest = () => MessageBoxResult.Cancel;
        await window.OpenFile(pythonPath);
        Require(editor.Text.Contains("Live alpha heading"), "Cancel protects draft when opening another file");
        window.ConfirmUnsavedForTest = () => MessageBoxResult.No;
        await window.OpenFile(Path.Combine(Path.GetDirectoryName(path)!, "missing.md"));
        Require(editor.Text.Contains("Live alpha heading"), "Failed open preserves the draft");
        Task pendingOpen = window.OpenFile(Path.Combine(Path.GetDirectoryName(path)!, "many.md"));
        window.ConfirmUnsavedForTest = null;
        Require(await window.SaveDocument(path), "Save from the editor succeeds");
        await pendingOpen;
        Require(File.ReadAllText(path).Contains("Live alpha heading") && !window.Title.Contains(" *"), "Saved contents and dirty state");
        Require(window.Title.StartsWith("README", StringComparison.Ordinal), "Saving cancels a pending document switch");
        await window.OpenFile(pythonPath);
        Require(editor.IsReadOnly && !Control<StackPanel>("FormattingTools").IsVisible && !Control<Button>("SaveButton").IsVisible,
            "Source files expose only read-only tools");
        Require(!await window.SaveDocument(pythonPath), "Source files cannot be saved through the editor");
        Click("GoToButton");
        Control<TextBox>("LineBox").Text = "3";
        Require(ClickButton(Control<StackPanel>("LineEntry"), "Go"), "Jump button exists");
        Require(editor.SelectionStart == editor.Text.IndexOf("third", StringComparison.Ordinal), "Go to jumps to a logical line");
        Click("WrapButton");
        Require(editor.TextWrapping == TextWrapping.Wrap, "Toggle wrapping for code");
        await window.OpenFile(Path.Combine(Path.GetDirectoryName(path)!, "Editable.text"));
        Require(!editor.IsReadOnly && !Control<StackPanel>("FormattingTools").IsVisible && Control<Button>("SaveButton").IsVisible,
            "Plain text is editable and has no Markdown formatting controls");
        if (screenshots is not null)
        {
            Click("HomeButton");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var welcome = Control<ScrollViewer>("Welcome");
            Require(ClickButton(welcome, "Open a Python example →"), "Python example is accessible from Home");
            await Task.Delay(200, token);
            Capture(window, screenshots, "source.png");
            ChooseTheme(window, "Graphite");
            Click("FindButton");
            Control<TextBox>("FindBox").Text = "reading_time";
            await WaitUntil(() => Control<TextBlock>("FindCount").Text == "1 of 2", token);
            Capture(window, screenshots, "source-search.png");
            Click("HomeButton");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Require(ClickButton(welcome, "Try a sample →"), "Editor example is accessible from Home");
            Click("SplitButton");
            await Task.Delay(200, token);
            Capture(window, screenshots, "editor.png");
            ChooseTheme(window, "Paper");
            Click("SourceButton");
            int word = editor.Text.IndexOf("editable", StringComparison.Ordinal);
            editor.Select(word, "editable".Length);
            Click("BoldButton");
            await Task.Delay(200, token);
            Capture(window, screenshots, "format.png");
            Click("BoldButton");
            Click("ReadButton");
            ChooseTheme(window, "Dusk");
            Click("FindButton");
            Control<TextBox>("FindBox").Text = "sample";
            await WaitUntil(() => Control<TextBlock>("FindCount").Text.StartsWith("1 of ", StringComparison.Ordinal), token);
            Capture(window, screenshots, "find.png");
            Click("CloseFindButton");
            ChooseTheme(window, "Moss");
            Click("RailButton");
            Click("ZoomInButton");
            Click("ZoomInButton");
            await Task.Delay(200, token);
            Capture(window, screenshots, "focus.png");
            Click("ZoomLabel");
            Click("RailButton");
        }
    }

    private static void ChooseTheme(MainWindow window, string theme)
    {
        var themes = (WrapPanel)window.FindName("ThemeButtons");
        themes.Children.OfType<Button>().Single(button => Equals(button.Tag, theme)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitUntil(Func<bool> condition, CancellationToken token)
    {
        for (int attempt = 0; attempt < 50 && !condition(); attempt++) await Task.Delay(100, token);
        Require(condition(), "Live preview updates within five seconds");
    }

    private static bool ClickButton(DependencyObject root, string label)
    {
        if (root is Button button && Equals(button.Content, label))
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (ClickButton(VisualTreeHelper.GetChild(root, i), label)) return true;
        return false;
    }

    private static void Capture(Window window, string destination, string file)
    {
        window.UpdateLayout();
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(destination);
        using var output = File.Create(Path.Combine(destination, file));
        encoder.Save(output);
    }

    private static async Task MustReject(string path, CancellationToken token)
    {
        try { await ReaderFile.OpenAsync(path, token); }
        catch (IOException) { return; }
        throw new InvalidOperationException("Unexpectedly opened " + Path.GetFileName(path));
    }

    private static void Require(bool condition, string expectation)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + expectation);
    }
}
