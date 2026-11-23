using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Markdig.Syntax;
using Microsoft.Win32;

namespace TheReadme;

public partial class MainWindow : Window
{
    private const string Sample = """
        # The Readme

        This is an editable Markdown sample. Click a word and start typing, or choose **Split** for a live preview beside the draft.

        ## Getting started

        Open a file with **Ctrl+O**, drop it into this window, or launch it from a terminal:

        ```powershell
        readme ./README.md
        ```

        > The sidebar lists headings. Select one to jump to that section.

        ## Try the editor

        1. Select a word and use **B** or *I* in the toolbar.
        2. Place the cursor on a line and pick a size from **Text style**.
        3. Press **Ctrl+Shift+S** to save your own copy.

        | Feature | Shortcut |
        | --- | --- |
        | Save changes | Ctrl+S |
        | Find or replace | Ctrl+F / Ctrl+H |
        | Read / Edit / Split | Ctrl+1 / Ctrl+2 / Ctrl+3 |
        | Jump to a source line | Ctrl+G |

        - [x] Open the sample
        - [ ] Change the style of a heading
        - [ ] Try Ocean or Dusk in Settings

        ## Read your way

        Press **Ctrl+B** to toggle the outline. Zoom with **Ctrl+mouse wheel** or the toolbar buttons. The text reflows when you change the window size or zoom.

        Edit a source file such as `.py`, `.cpp`, `.java`, or `.hdl` with search, wrapping, and Go to line.

        ## Windows integration

        After installing, open **Settings** to choose file associations or manage the `readme` terminal command.

        [Return to the top](#the-readme) · [Visit the project on GitHub](https://github.com/shardapps/the-readme)
        """;

    private static readonly string[] ColorKeys = ["Base", "Surface", "Soft", "Line", "Ink", "Muted", "Accent", "OnAccent", "Hover"];
    private static readonly Dictionary<string, string[]> Themes = new()
    {
        ["Lilac"] = ["#F4F1FA", "#FEFCFF", "#F1EAFB", "#DBD1EC", "#231933", "#594A70", "#6034BA", "#FFFFFF", "#E9DDF8"],
        ["Paper"] = ["#F7F1E6", "#FFFDF7", "#F4EAD9", "#E1D2BD", "#302219", "#685441", "#884624", "#FFFFFF", "#EEE0CD"],
        ["Midnight"] = ["#161320", "#20192D", "#2C2340", "#463A5A", "#F8F2FF", "#CABCE0", "#D2B9FF", "#241334", "#433359"],
        ["Moss"] = ["#EAF1EA", "#FAFDF8", "#E3EDE2", "#CDDCCD", "#1A3025", "#435E4D", "#276243", "#FFFFFF", "#D6E7D8"],
        ["Ocean"] = ["#EAF3F9", "#FAFDFF", "#E0EDF6", "#C7DDEB", "#142D40", "#405E75", "#185E89", "#FFFFFF", "#D0E7F6"],
        ["Dusk"] = ["#26191E", "#332329", "#452B35", "#634352", "#FFF0F4", "#E6C3CC", "#FFB79E", "#3C2019", "#543540"],
        ["Graphite"] = ["#14191E", "#1E242B", "#29323C", "#414D5C", "#F2F7FC", "#C1CCDA", "#AAD9FF", "#142737", "#334454"]
    };

    private readonly ReaderPreferences preferences;
    private readonly SemaphoreSlim readerGate = new(1);
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(130) };
    private readonly List<(int Page, int Offset, int Occurrence)> matches = [];
    private ReaderFile? currentFile;
    private MarkdownDocument? currentMarkdown;
    private IReadOnlyDictionary<string, ImageSource> currentImages = new Dictionary<string, ImageSource>();
    private IReadOnlyList<OutlineItem> visibleOutline = [];
    private MarkdownPages? previewPages;
    private int previewPage;
    private DocumentSearch? searchIndex;
    private CancellationTokenSource? loading;
    private string? pendingAnchor;
    private int matchIndex;

    private sealed record RailEntry(string Label, string Hint, Thickness Indent, MarkdownPages.Heading? Heading = null, string? File = null);

    public MainWindow(string? initialPath) : this(initialPath, ReaderPreferences.Load()) { }

    internal MainWindow(string? initialPath, ReaderPreferences preferences)
    {
        this.preferences = preferences;
        InitializeComponent();
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Search(); };
        previewDelay.Tick += (_, _) => { previewDelay.Stop(); _ = RefreshDraftPreview(); };
        RecentToggle.IsChecked = preferences.RememberRecent;
        AutoIndentToggle.IsChecked = preferences.AutoIndentSource;
        MotionToggle.IsChecked = preferences.AnimateTransitions;
        TypeToEditToggle.IsChecked = preferences.TypeToEdit;
        OpeningViewBox.SelectedItem = OpeningViewBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, preferences.OpeningView.ToString())) ?? OpeningViewBox.Items[0];
        foreach (string theme in Themes.Keys.Prepend("System"))
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            Brush swatch = theme == "System" ? SystemColors.HighlightBrush : new SolidColorBrush((Color)ColorConverter.ConvertFromString(Themes[theme][6]));
            label.Children.Add(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = swatch, Margin = new Thickness(0, 0, 6, 0) });
            label.Children.Add(new TextBlock { Text = theme, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = label, Tag = theme, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 1, 3, 2) };
            System.Windows.Automation.AutomationProperties.SetName(button, theme);
            button.Click += (_, _) => { preferences.Theme = theme; preferences.Save(); ApplyTheme(); };
            ThemeButtons.Children.Add(button);
        }
        SystemParameters.StaticPropertyChanged += SystemSettingsChanged;
        SystemEvents.UserPreferenceChanged += WindowsPreferencesChanged;
        ApplyTheme();
        SetZoom(preferences.Zoom, save: false);
        ShowRail();
        RefreshRail();
        if (initialPath is null) LoadWelcomeArt();
        if (initialPath is not null)
            Loaded += async (_, _) =>
            {
                await OpenFile(initialPath);
                if (currentFile is null && WelcomeArt.Background is null) LoadWelcomeArt();
            };
        StateChanged += (_, _) => { Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; };
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemSettingsChanged;
        SystemEvents.UserPreferenceChanged -= WindowsPreferencesChanged;
        searchDelay.Stop();
        CancelLoad();
        CancelPreview();
        base.OnClosed(e);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(DismissFromWindowChrome);
    }

    private nint DismissFromWindowChrome(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        const int nonClientLeftDown = 0x00A1;
        const int nonClientRightDown = 0x00A4;
        if (message is nonClientLeftDown or nonClientRightDown) DismissSettings();
        return 0;
    }

    private void SystemSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.InvokeAsync(ApplyTheme);
    }

    private void WindowsPreferencesChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
            Dispatcher.InvokeAsync(() => { if (preferences.Theme == "System") ApplyTheme(); });
    }

    private void ApplyTheme()
    {
        string selected = preferences.Theme ?? "System";
        if (selected == "System")
        {
            using var personalize = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            selected = personalize?.GetValue("AppsUseLightTheme") is 0 ? "Midnight" : "Lilac";
        }
        string[] palette = Themes.TryGetValue(selected, out var matching) ? matching : Themes["Lilac"];
        Brush[] highContrast = [SystemColors.WindowBrush, SystemColors.WindowBrush, SystemColors.ControlBrush,
            SystemColors.WindowTextBrush, SystemColors.WindowTextBrush, SystemColors.WindowTextBrush,
            SystemColors.HighlightBrush, SystemColors.HighlightTextBrush, SystemColors.ControlBrush];
        for (int i = 0; i < ColorKeys.Length; i++)
        {
            var brush = SystemParameters.HighContrast ? highContrast[i] : new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
            brush.Freeze();
            Resources[ColorKeys[i]] = brush;
        }
        foreach (Button button in ThemeButtons.Children)
        {
            bool active = Equals(button.Tag, preferences.Theme);
            button.Background = active ? GetBrush("Hover") : Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetItemStatus(button, active ? "Selected" : "");
        }
        RefreshMode();
        if (currentFile?.Format.Kind == FileKind.Markdown)
        {
            var scroll = FindScrollViewer(Preview);
            double offset = scroll?.VerticalOffset ?? 0;
            RenderPreview();
            if (offset > 0) Dispatcher.InvokeAsync(() => FindScrollViewer(Preview)?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
            if (FindBar.Visibility == Visibility.Visible) Search();
        }
    }

    private Brush GetBrush(string key) => (Brush)Resources[key];

    internal async Task OpenFile(string path, string? anchor = null) => await LoadFile(path, anchor);

    private async Task LoadFile(string path, string? anchor = null, ViewMode? view = null)
    {
        if (!await CanLeaveDocument()) return;
        CancelLoad();
        var operation = new CancellationTokenSource();
        loading = operation;
        try
        {
            Status.Text = "Opening " + Path.GetFileName(path) + "…";
            var result = await Task.Run(async () =>
            {
                var token = operation.Token;
                await readerGate.WaitAsync(token);
                try
                {
                    var file = await ReaderFile.OpenAsync(path, token);
                    token.ThrowIfCancellationRequested();
                    var markdown = file.IsPlainText ? null : MarkdownView.Parse(file.Text);
                    token.ThrowIfCancellationRequested();
                    var images = MarkdownView.LoadImages(markdown, file, token);
                    return (file, markdown, images);
                }
                finally { readerGate.Release(); }
            }, operation.Token);
            if (loading != operation || operation.IsCancellationRequested) return;
            pendingAnchor = anchor;
            Display(result.file, result.markdown, result.images, view: anchor is not null && (view ?? PreferredView) == ViewMode.Edit ? ViewMode.Read : view);
            preferences.Record(result.file.Path);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            if (loading == operation) Status.Text = "Could not open file. " + ex.Message;
        }
        finally
        {
            if (loading == operation) loading = null;
            operation.Dispose();
        }
    }

    private void CancelLoad()
    {
        loading?.Cancel();
        loading = null;
    }

    private void Display(ReaderFile file, MarkdownDocument? markdown, IReadOnlyDictionary<string, ImageSource>? images, bool sample = false, ViewMode? view = null)
    {
        CancelPreview();
        currentFile = file;
        currentMarkdown = markdown;
        previewPages = null;
        previewPage = 0;
        currentImages = images ?? new Dictionary<string, ImageSource>();
        mode = file.Format.Kind == FileKind.Markdown ? view ?? (sample ? ViewMode.Read : PreferredView) : ViewMode.Edit;
        LoadEditor(file.Text, loadNow: mode != ViewMode.Read);
        savedText = renderedText = file.Text;
        dirty = false;
        FindBar.Visibility = Visibility.Collapsed;
        ReplaceRow.Visibility = Visibility.Collapsed;
        LineEntry.Visibility = Visibility.Collapsed;
        FindBox.Clear();
        ReplaceBox.Clear();
        searchDelay.Stop();
        RenderPreview();
        FileTitle.Text = sample ? "Sample document" : file.Name;
        FileLocation.Text = file.IsOnDisk ? file.Path : sample ? "The Readme · sample" : "New document";
        FileLocation.ToolTip = file.IsOnDisk ? file.Path : null;
        Welcome.Visibility = Visibility.Collapsed;
        WelcomeArt.Background = null;
        DocumentArea.Visibility = Visibility.Visible;
        ReloadButton.IsEnabled = RevealButton.IsEnabled = file.IsOnDisk;
        FindButton.IsEnabled = true;
        RefreshMode();
        RefreshDocumentLabel();
        Status.Text = sample ? $"Sample · {file.Format.Label}" : FileStatus(file);
        FadeIn(DocumentArea, travel: 4);
        if (mode == ViewMode.Read) Preview.Focus();
        else Source.Focus();
        if (pendingAnchor is { } anchor)
        {
            pendingAnchor = null;
            JumpTo(anchor);
        }
    }

    private void RenderPreview(bool updateOutline = true)
    {
        if (currentFile?.Format.Kind != FileKind.Markdown)
        {
            Preview.Document = null;
            visibleOutline = [];
            previewPages = null;
            searchIndex = null;
            UpdatePageButtons();
            RefreshRail();
            return;
        }
        if (previewPages is null && currentMarkdown is not null)
            previewPages = new MarkdownPages(currentMarkdown, currentImages);
        previewPage = Math.Min(previewPage, (previewPages?.Count ?? 1) - 1);
        var view = new MarkdownView(this, currentFile!, currentImages, OpenLink);
        Preview.Document = view.Render(previewPages?.Blocks(previewPage));
        visibleOutline = view.Outline;
        searchIndex = null;
        UpdatePageButtons();
        if (updateOutline) RefreshRail();
    }

    private void UpdatePageButtons()
    {
        int count = previewPages?.Count ?? 1;
        PageControls.Visibility = mode != ViewMode.Edit && count > 1 ? Visibility.Visible : Visibility.Collapsed;
        PreviousPageButton.IsEnabled = previewPage > 0;
        NextPageButton.IsEnabled = previewPage + 1 < count;
        PreviewPageLabel.Text = $"{previewPage + 1} / {count}";
    }

    private void PageBack(object sender, RoutedEventArgs e) => MovePage(-1);
    private void PageNext(object sender, RoutedEventArgs e) => MovePage(1);

    private void MovePage(int direction)
    {
        int next = previewPage + direction;
        if (mode != ViewMode.Edit && previewPages is { } pages && next >= 0 && next < pages.Count)
        {
            ShowPage(next);
            FadeIn(Preview, travel: 3);
        }
    }

    private void ShowPage(int page)
    {
        if (page == previewPage) return;
        previewPage = page;
        RenderPreview(updateOutline: false);
        FindScrollViewer(Preview)?.ScrollToTop();
    }

    private void RefreshRail()
    {
        ToolbarHomeButton.Visibility = HomeButton.Visibility = HomeDivider.Visibility =
            currentFile is null ? Visibility.Collapsed : Visibility.Visible;
        bool showOutline = currentFile?.Format.Kind == FileKind.Markdown;
        RailHeading.Text = showOutline ? previewPages?.Count > 1 ? "Headings" : "On this page" : "Recent files";
        RailEntry[] entries = !showOutline
            ? preferences.Recent.Select(path => new RailEntry(Path.GetFileName(path), path, new Thickness(), File: path)).ToArray()
            : (previewPages?.Headings ?? []).Select(heading => new RailEntry(heading.Label, heading.Label,
                new Thickness(Math.Min(heading.Level - 1, 4) * 9, 0, 0, 0), heading)).ToArray();
        RailList.ItemsSource = entries;
        RailList.Visibility = entries.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        RailEmpty.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RailEmpty.Text = showOutline ? "This file has no headings." :
            preferences.RememberRecent ? "Files you open appear here." : "Recent files are turned off.";
    }

    private async void SelectRail(object sender, SelectionChangedEventArgs e)
    {
        if (RailList.SelectedItem is not RailEntry entry) return;
        if (entry.Heading is { } heading)
        {
            if (mode == ViewMode.Edit) await SetMode(ViewMode.Read);
            JumpTo(heading.Anchor);
        }
        else if (entry.File is { } path)
        {
            RailList.SelectedIndex = -1;
            await OpenFile(path);
        }
    }

    private async void PickFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a document or source file",
            Filter = FileFormats.OpenFilter,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) await OpenFile(dialog.FileName);
    }

    private async void OpenSample(object sender, RoutedEventArgs e)
    {
        if (!await CanLeaveDocument()) return;
        CancelLoad();
        Display(ReaderFile.Untitled("README.md", Sample),
            MarkdownView.Parse(Sample), null, sample: true);
    }

    private async void Reload(object sender, RoutedEventArgs e)
    {
        if (currentFile is { IsOnDisk: true } file) await LoadFile(file.Path, view: mode);
    }

    private void RevealFile(object sender, RoutedEventArgs e)
    {
        if (currentFile is not { IsOnDisk: true } file) return;
        try
        {
            var explorer = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            explorer.Arguments = "/select,\"" + file.Path + "\"";
            Process.Start(explorer);
        }
        catch (Win32Exception ex) { Status.Text = ex.Message; }
    }

    private void CheckDrop(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OpenDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) await OpenFile(paths[0]);
    }

    private async void GoHome(object sender, RoutedEventArgs e)
    {
        if (!await CanLeaveDocument()) return;
        CancelLoad();
        CancelPreview();
        currentFile = null;
        dirty = false;
        currentMarkdown = null;
        previewPages = null;
        previewPage = 0;
        currentImages = new Dictionary<string, ImageSource>();
        searchIndex = null;
        visibleOutline = [];
        Preview.Document = null;
        LoadEditor("", loadNow: false);
        FindBox.Clear();
        searchDelay.Stop();
        matches.Clear();
        FindBar.Visibility = DocumentArea.Visibility = Visibility.Collapsed;
        Welcome.Visibility = Visibility.Visible;
        LoadWelcomeArt();
        ReloadButton.IsEnabled = FindButton.IsEnabled = false;
        RefreshMode();
        Caption.Text = "Home";
        Title = "The Readme";
        Status.Text = "Ready to read";
        RefreshRail();
        FadeIn(Welcome, travel: 5);
    }

    private void LoadWelcomeArt()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/Readme;component/Assets/cover.jpg");
        image.DecodePixelWidth = 600;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        brush.Freeze();
        WelcomeArt.Background = brush;
    }

    private void ToggleRail(object sender, RoutedEventArgs e)
    {
        preferences.ShowSidebar = !preferences.ShowSidebar;
        preferences.Save();
        ShowRail();
        if (preferences.ShowSidebar) FadeIn(Rail, travel: 3);
    }

    private void ShowRail()
    {
        RailWidth.Width = new GridLength(preferences.ShowSidebar ? 219 : 0);
        Rail.Visibility = preferences.ShowSidebar ? Visibility.Visible : Visibility.Collapsed;
        ArrangeSurface();
    }

    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        bool show = SettingsPane.Visibility != Visibility.Visible;
        SettingsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            RefreshPath();
            FadeIn(SettingsPane, travel: 5);
            ThemeButtons.Children.OfType<Button>().First().Focus();
        }
        else SettingsButton.Focus();
    }

    private void DismissSettingsOnClick(object sender, MouseButtonEventArgs e) => DismissSettingsAt(e.GetPosition(this));
    private void DismissSettingsOnDeactivate(object? sender, EventArgs e) => DismissSettings();

    internal void DismissSettingsAt(Point position)
    {
        if (!SettingsPane.IsVisible) return;
        bool Hit(FrameworkElement element) => new Rect(element.TranslatePoint(new Point(), this), element.RenderSize).Contains(position);
        if (!Hit(SettingsPane) && !Hit(SettingsButton)) DismissSettings();
    }

    private void DismissSettings() => SettingsPane.Visibility = Visibility.Collapsed;

    private void OpenDefaultApps(object sender, RoutedEventArgs e)
    {
        try { WindowsIntegration.Open(WindowsIntegration.IsInstalled || WindowsIntegration.IsPackaged ? WindowsIntegration.DefaultAppsUri : "ms-settings:defaultapps"); }
        catch (Win32Exception ex) { Status.Text = ex.Message; }
    }

    private void RefreshPath()
    {
        if (WindowsIntegration.IsPackaged)
        {
            PathButton.IsEnabled = true;
            PathButton.Content = "Manage terminal command ↗";
            PathHint.Text = "Use readme in a terminal. Windows manages this command in App execution aliases.";
            return;
        }
        if (!WindowsIntegration.IsInstalled)
        {
            PathButton.IsEnabled = false;
            PathHint.Text = "Install The Readme to use the readme command.";
            return;
        }
        string state = WindowsIntegration.PathStatus();
        PathButton.IsEnabled = state != "Already on PATH";
        PathButton.Content = state switch { "On" => "Remove from PATH", "Off" => "Add to PATH", _ => "Already on PATH" };
        PathHint.Text = state == "Off" ? "Open new files with readme from your terminal." : "The readme command is available in new terminal windows.";
    }

    private async void TogglePath(object sender, RoutedEventArgs e)
    {
        if (WindowsIntegration.IsPackaged)
        {
            try { WindowsIntegration.Open(WindowsIntegration.ExecutionAliasSettingsUri); }
            catch (Win32Exception ex) { Status.Text = ex.Message; }
            return;
        }
        PathButton.IsEnabled = false;
        try { await Task.Run(() => WindowsIntegration.SetPath(WindowsIntegration.PathStatus() == "Off")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { Status.Text = ex.Message; }
        finally { RefreshPath(); }
    }

    private void ChangeRecent(object sender, RoutedEventArgs e)
    {
        preferences.RememberRecent = RecentToggle.IsChecked == true;
        if (!preferences.RememberRecent) preferences.Recent.Clear();
        preferences.Save();
        RefreshRail();
    }

    private void ChangeEditorOptions(object sender, RoutedEventArgs e)
    {
        preferences.AutoIndentSource = AutoIndentToggle.IsChecked == true;
        preferences.AnimateTransitions = MotionToggle.IsChecked == true;
        preferences.TypeToEdit = TypeToEditToggle.IsChecked == true;
        preferences.Save();
    }

    private void ClearRecent(object sender, RoutedEventArgs e)
    {
        preferences.Recent.Clear();
        preferences.Save();
        RefreshRail();
        Status.Text = "Recent files cleared";
    }

    private void OpenFind(object sender, RoutedEventArgs e)
    {
        if (currentFile is null) return;
        bool wasOpen = FindBar.IsVisible;
        FindBar.Visibility = Visibility.Visible;
        if (!wasOpen) FadeIn(FindBar, travel: 3);
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        ReplaceRow.Visibility = Visibility.Collapsed;
        FindBox.Clear();
        searchDelay.Stop();
        Search();
        FindButton.Focus();
    }

    private void SearchChanged(object sender, RoutedEventArgs e)
    {
        searchDelay.Stop();
        searchDelay.Start();
    }

    private void SearchCaseChanged(object sender, RoutedEventArgs e) => SearchChanged(sender, e);

    private StringComparison SearchComparison => MatchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private void Search(int from = 0)
    {
        matches.Clear();
        matchIndex = 0;
        string query = FindBox.Text;
        if (query.Length == 0 || currentFile is null)
        {
            FindCount.Text = "";
            if (Preview.Selection is { } selection) selection.Select(selection.Start, selection.Start);
            Source.Select(Source.SelectionStart, 0);
            return;
        }
        var comparison = SearchComparison;
        if (mode != ViewMode.Read) AddMatches(Source.Text, 0);
        else
        {
            int pageCount = previewPages?.Count ?? 1;
            for (int page = 0; page < pageCount && matches.Count < 1000; page++)
                AddMatches(page == previewPage ? RenderedSearch().Text : previewPages!.SearchText(page), page);
        }
        if (from > 0) matchIndex = Math.Max(0, matches.FindIndex(match => match.Offset >= from));
        SelectMatch();

        void AddMatches(string text, int page)
        {
            int position = 0;
            int occurrence = 0;
            while (matches.Count < 1000 && (position = text.IndexOf(query, position, comparison)) >= 0)
            {
                matches.Add((page, position, occurrence++));
                position += query.Length;
            }
        }
    }

    private DocumentSearch RenderedSearch() => searchIndex ??= new DocumentSearch(Preview.Document);

    private void FindPrevious(object sender, RoutedEventArgs e) => MoveMatch(-1);
    private void FindNext(object sender, RoutedEventArgs e) => MoveMatch(1);
    private void MoveMatch(int step)
    {
        if (searchDelay.IsEnabled) { searchDelay.Stop(); Search(); return; }
        if (matches.Count == 0) return;
        matchIndex = (matchIndex + step + matches.Count) % matches.Count;
        SelectMatch();
    }

    private void SelectMatch()
    {
        if (matches.Count == 0)
        {
            FindCount.Text = "No matches";
            if (Preview.Selection is { } selection) selection.Select(selection.Start, selection.Start);
            Source.Select(Source.SelectionStart, 0);
            return;
        }
        var match = matches[matchIndex];
        int start = match.Offset;
        int length = FindBox.Text.Length;
        FindCount.Text = $"{matchIndex + 1} of {matches.Count}{(matches.Count == 1000 ? "+" : "")}";
        if (mode != ViewMode.Read)
        {
            Source.Select(start, length);
            int line = Source.GetLineIndexFromCharacterIndex(start);
            if (line >= 0) Source.ScrollToLine(line);
        }
        else
        {
            ShowPage(match.Page);
            var index = RenderedSearch();
            string query = FindBox.Text;
            if (start + length > index.Text.Length ||
                string.Compare(index.Text, start, query, 0, length, SearchComparison) != 0)
            {
                start = index.Text.IndexOf(query, SearchComparison);
                for (int occurrence = 0; start >= 0 && occurrence < match.Occurrence; occurrence++)
                    start = index.Text.IndexOf(query, start + length, SearchComparison);
                if (start < 0) return;
            }
            if (index.PointerAt(start, end: false) is { } first && index.PointerAt(start + length, end: true) is { } last)
            {
                Preview.Selection?.Select(first, last);
                first.Paragraph?.BringIntoView();
            }
        }
    }

    private void JumpTo(string fragment)
    {
        if (previewPages?.Headings.FirstOrDefault(item => item.Anchor.Equals(fragment, StringComparison.OrdinalIgnoreCase)) is { } heading)
        {
            ShowPage(heading.Page);
            if (visibleOutline.FirstOrDefault(item => item.Anchor.Equals(fragment, StringComparison.OrdinalIgnoreCase)) is { } target)
                Dispatcher.InvokeAsync(() => target.Target.BringIntoView(), DispatcherPriority.Loaded);
            return;
        }
        Status.Text = "Heading not found: " + fragment;
    }

    private async void OpenLink(string destination)
    {
        if (destination.Length > 0 && destination[0] == '#') { JumpTo(Uri.UnescapeDataString(destination.Substring(1))); return; }
        if (DocumentLinks.TryExternal(destination, out Uri? uri))
        {
            try { WindowsIntegration.Open(uri!.AbsoluteUri); }
            catch (Win32Exception ex) { Status.Text = ex.Message; }
            return;
        }
        if (currentFile is { IsOnDisk: true } file && DocumentLinks.LocalPath(file.Path, destination) is { } path && FileFormats.CanRead(path))
        {
            int hash = destination.IndexOf('#');
            await OpenFile(path, hash < 0 ? null : Uri.UnescapeDataString(destination.Substring(hash + 1)));
        }
        else Status.Text = "This link points outside the supported document folder or uses an unsupported format.";
    }

    private void SetZoom(int value, bool save = true)
    {
        preferences.Zoom = Math.Max(60, Math.Min(value, 220));
        Preview.Zoom = preferences.Zoom;
        Source.FontSize = 15.0 * preferences.Zoom / 100;
        ZoomLabel.Content = preferences.Zoom + "%";
        if (save) preferences.Save();
    }

    private void ZoomWithWheel(object sender, MouseWheelEventArgs e)
    {
        if (unsavedPrompt is not null || !DocumentArea.IsVisible || SettingsPane.IsVisible || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        SetZoom(preferences.Zoom + (e.Delta > 0 ? 10 : -10));
        e.Handled = true;
    }

    private void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(preferences.Zoom + 10);
    private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(preferences.Zoom - 10);
    private void ZoomReset(object sender, RoutedEventArgs e) => SetZoom(100);

    private async void HandleKey(object sender, KeyEventArgs e)
    {
        if (unsavedPrompt is not null)
        {
            if (e.Key == Key.Escape)
            {
                unsavedPrompt.TrySetResult(UnsavedChoice.Cancel);
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (SettingsPane.IsVisible) ToggleSettings(sender, e);
            else if (LineEntry.IsVisible) { LineEntry.Visibility = Visibility.Collapsed; Source.Focus(); }
            else if (FindBar.IsVisible) CloseFind(sender, e);
            else return;
        }
        else if (e.Key == Key.Enter && FindBox.IsKeyboardFocusWithin) MoveMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        else if (e.Key == Key.Enter && ReplaceBox.IsKeyboardFocusWithin) ReplaceMatches(all: false);
        else if (e.Key == Key.Enter && LineBox.IsKeyboardFocusWithin) GoToLine();
        else if (e.Key == Key.Enter && Source.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = AutoIndentSource();
            return;
        }
        else if (e.Key == Key.F5) Reload(sender, e);
        else if (e.Key == Key.System && e.SystemKey == Key.Home && Keyboard.Modifiers == ModifierKeys.Alt) GoHome(sender, e);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            switch (e.Key)
            {
                case Key.O: PickFile(sender, e); break;
                case Key.N: NewDocument(sender, e); break;
                case Key.F: OpenFind(sender, e); break;
                case Key.H: OpenReplace(sender, e); break;
                case Key.D1 or Key.NumPad1: e.Handled = true; await ChooseView(ViewMode.Read); return;
                case Key.D2 or Key.NumPad2: e.Handled = true; await ChooseView(ViewMode.Edit); return;
                case Key.D3 or Key.NumPad3: e.Handled = true; await ChooseView(ViewMode.Split); return;
                case Key.PageUp: MovePage(-1); break;
                case Key.PageDown: MovePage(1); break;
                case Key.G: OpenGoTo(sender, e); break;
                case Key.S when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): SaveAsClicked(sender, e); break;
                case Key.S: SaveClicked(sender, e); break;
                case Key.B when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): ApplyInline("**"); break;
                case Key.B: ToggleRail(sender, e); break;
                case Key.I when Source.IsKeyboardFocusWithin: ApplyInline("*"); break;
                case Key.U when Source.IsKeyboardFocusWithin: ApplyInline("++"); break;
                case Key.Z when Source.IsKeyboardFocusWithin && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift): Source.Redo(); break;
                case Key.Z when Source.IsKeyboardFocusWithin: Source.Undo(); break;
                case Key.Y when Source.IsKeyboardFocusWithin: Source.Redo(); break;
                case Key.OemComma: ToggleSettings(sender, e); break;
                case Key.OemPlus or Key.Add: ZoomIn(sender, e); break;
                case Key.OemMinus or Key.Subtract: ZoomOut(sender, e); break;
                case Key.D0 or Key.NumPad0: ZoomReset(sender, e); break;
                default: return;
            }
        }
        else return;
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
        return null;
    }

    private void FadeIn(UIElement element, double travel = 0)
    {
        if (!preferences.AnimateTransitions || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        bool reduced = RenderPolicy.LimitMotion((int)(ActualWidth * dpi.DpiScaleX), (int)(ActualHeight * dpi.DpiScaleY));
        var duration = TimeSpan.FromMilliseconds(reduced ? 120 : travel == 0 ? 145 : 190);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        // Opacity on a full reading surface allocates a second window-sized render target.
        bool isDocumentSurface = element == DocumentArea || element == Welcome || element == Preview || element == Source;
        if (!isDocumentSurface)
        {
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.87, 1, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            });
        }
        else if (travel == 0) travel = 2;
        if (travel == 0) return;
        var offset = new TranslateTransform();
        element.RenderTransform = offset;
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(reduced ? Math.Min(2, travel) : travel, 0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        });
    }

    private void ClipHero(object sender, SizeChangedEventArgs e) =>
        ((Border)sender).Clip = new RectangleGeometry(new Rect(e.NewSize), 17, 17);
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ToggleMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
