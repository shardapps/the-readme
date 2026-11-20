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
                    Console.WriteLine("File, rendering, search, link, settings and cancellation checks passed.");
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

        var crowded = new ReaderFile(largePath, File.ReadAllText(largePath), false, 0);
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

        if (screenshots is not null)
        {
            ((ScrollViewer)window.FindName("Welcome")).Visibility = Visibility.Visible;
            Require(ClickButton((ScrollViewer)window.FindName("Welcome"), "Try a sample →"), "Sample button exists");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(200, token);
            Capture(window, screenshots, "lilac.png");
        }
        window.Close();
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
