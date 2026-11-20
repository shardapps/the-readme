using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Markdig.Syntax;
using Microsoft.Win32;

namespace TheReadme;

public partial class MainWindow : Window
{
    private const string Sample = """
        # The Readme

        A small Windows reader for Markdown and README files. This sample shows how the app lays out a document.

        ## Getting started

        Open a file with **Ctrl+O**, drop it into this window, or launch it from a terminal:

        ```powershell
        readme ./README.md
        ```

        > The sidebar lists headings. Select one to jump to that section.

        ## Read your way

        | Feature | Shortcut |
        | --- | --- |
        | Find in the document | Ctrl+F |
        | Show or hide the outline | Ctrl+B |
        | Reset the reading zoom | Ctrl+0 |

        - [x] Read formatted Markdown
        - [ ] Try a different theme in Settings

        ## Windows integration

        After installing, open **Settings** to set The Readme as your default Markdown viewer or add the terminal command to your PATH.

        [Return to the top](#the-readme) · [Visit the project on GitHub](https://github.com/shardapps/the-readme)
        """;

    private static readonly string[] ColorKeys = ["Base", "Surface", "Soft", "Line", "Ink", "Muted", "Accent", "OnAccent", "Hover"];
    private static readonly Dictionary<string, string[]> Themes = new()
    {
        ["Lilac"] = ["#F8F7FC", "#FFFFFF", "#F0EDF8", "#DFDAEA", "#211B2F", "#635B71", "#6243BD", "#FFFFFF", "#E7E1F5"],
        ["Paper"] = ["#FAF6EE", "#FFFDF7", "#F5EDDF", "#E3D8C8", "#2C221B", "#6E5B4A", "#884929", "#FFFFFF", "#EEE2D2"],
        ["Midnight"] = ["#171421", "#201A2B", "#2C233A", "#40354D", "#F3EAFB", "#C1B1CF", "#CDB1FF", "#241334", "#3C2E50"],
        ["Moss"] = ["#F0F4EF", "#FBFDF9", "#E6EEE5", "#D5E0D3", "#1D2D24", "#4E6356", "#31664D", "#FFFFFF", "#DDEBDC"]
    };

    private readonly ReaderPreferences preferences;
    private readonly SemaphoreSlim readerGate = new(1);
    private readonly DispatcherTimer searchDelay = new() { Interval = TimeSpan.FromMilliseconds(130) };
    private readonly List<int> matches = [];
    private ReaderFile? currentFile;
    private MarkdownDocument? currentMarkdown;
    private IReadOnlyDictionary<string, ImageSource> currentImages = new Dictionary<string, ImageSource>();
    private IReadOnlyList<OutlineItem> outline = [];
    private DocumentSearch? searchIndex;
    private CancellationTokenSource? loading;
    private string? pendingAnchor;
    private int matchIndex;
    private bool isSample;
    private bool showingSource;

    private sealed record RailEntry(string Label, string Hint, Thickness Indent, OutlineItem? Heading = null, string? File = null);

    public MainWindow(string? initialPath) : this(initialPath, ReaderPreferences.Load()) { }

    internal MainWindow(string? initialPath, ReaderPreferences preferences)
    {
        this.preferences = preferences;
        InitializeComponent();
        searchDelay.Tick += (_, _) => { searchDelay.Stop(); Search(); };
        RecentToggle.IsChecked = preferences.RememberRecent;
        foreach (string theme in Themes.Keys.Prepend("System"))
        {
            var button = new Button { Content = theme, Tag = theme, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 1, 3, 2) };
            button.Click += (_, _) => { preferences.Theme = theme; preferences.Save(); ApplyTheme(); };
            ThemeButtons.Children.Add(button);
        }
        SystemParameters.StaticPropertyChanged += SystemSettingsChanged;
        SystemEvents.UserPreferenceChanged += WindowsPreferencesChanged;
        ApplyTheme();
        SetZoom(preferences.Zoom, save: false);
        ShowRail();
        RefreshRail();
        if (initialPath is not null) Loaded += async (_, _) => await OpenFile(initialPath);
        StateChanged += (_, _) => { Frame.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0); MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922"; };
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= SystemSettingsChanged;
        SystemEvents.UserPreferenceChanged -= WindowsPreferencesChanged;
        searchDelay.Stop();
        CancelLoad();
        base.OnClosed(e);
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
        string[] palette = Themes.GetValueOrDefault(selected, Themes["Lilac"]);
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
        if (currentFile is not null)
        {
            var scroll = FindScrollViewer(Preview);
            double offset = scroll?.VerticalOffset ?? 0;
            RenderPreview();
            if (offset > 0) Dispatcher.InvokeAsync(() => FindScrollViewer(Preview)?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);
            if (FindBar.Visibility == Visibility.Visible) Search();
        }
    }

    private Brush GetBrush(string key) => (Brush)Resources[key];

    internal async Task OpenFile(string path, string? anchor = null)
    {
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
            Display(result.file, result.markdown, result.images, sample: false);
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

    private void Display(ReaderFile file, MarkdownDocument? markdown, IReadOnlyDictionary<string, ImageSource>? images, bool sample)
    {
        isSample = sample;
        currentFile = file;
        currentMarkdown = markdown;
        currentImages = images ?? new Dictionary<string, ImageSource>();
        showingSource = false;
        Source.Clear();
        FindBar.Visibility = Visibility.Collapsed;
        FindBox.Clear();
        searchDelay.Stop();
        RenderPreview();
        Caption.Text = file.Name;
        Title = file.Name + " · The Readme";
        FileTitle.Text = sample ? "Sample document" : file.Name;
        FileLocation.Text = sample ? "The Readme" : file.Path;
        FileLocation.ToolTip = sample ? null : file.Path;
        Welcome.Visibility = Visibility.Collapsed;
        DocumentArea.Visibility = Visibility.Visible;
        ReloadButton.IsEnabled = RevealButton.IsEnabled = !sample;
        FindButton.IsEnabled = ReadButton.IsEnabled = SourceButton.IsEnabled = true;
        RefreshMode();
        Status.Text = sample ? "Sample · Markdown" : $"{(file.IsPlainText ? "Plain text" : "Markdown")} · {file.Bytes / 1024.0:0.#} KB";
        FadeIn(DocumentArea);
        if (pendingAnchor is { } anchor)
        {
            pendingAnchor = null;
            JumpTo(anchor);
        }
    }

    private void RenderPreview()
    {
        var view = new MarkdownView(this, currentFile!, currentImages, OpenLink);
        Preview.Document = view.Render(currentMarkdown);
        outline = view.Outline;
        searchIndex = null;
        RefreshRail();
    }

    private void RefreshRail()
    {
        HomeButton.Visibility = HomeDivider.Visibility = currentFile is null ? Visibility.Collapsed : Visibility.Visible;
        RailHeading.Text = currentFile is null ? "Recent files" : "On this page";
        RailEntry[] entries = currentFile is null
            ? preferences.Recent.Select(path => new RailEntry(Path.GetFileName(path), path, new Thickness(), File: path)).ToArray()
            : outline.Select(heading => new RailEntry(heading.Label, heading.Label, new Thickness(Math.Min(heading.Level - 1, 4) * 9, 0, 0, 0), heading)).ToArray();
        RailList.ItemsSource = entries;
        RailList.Visibility = entries.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        RailEmpty.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RailEmpty.Text = currentFile is not null ? "This file has no headings." :
            preferences.RememberRecent ? "Files you open appear here." : "Recent files are turned off.";
    }

    private async void SelectRail(object sender, SelectionChangedEventArgs e)
    {
        if (RailList.SelectedItem is not RailEntry entry) return;
        if (entry.Heading is { } heading)
        {
            SetMode(source: false);
            await Dispatcher.InvokeAsync(() => heading.Target.BringIntoView(), DispatcherPriority.Loaded);
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
            Title = "Open a README or Markdown file",
            Filter = "Markdown and text|*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdwn;*.mdtxt;*.txt;README;CHANGELOG;LICENSE|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) await OpenFile(dialog.FileName);
    }

    private void OpenSample(object sender, RoutedEventArgs e)
    {
        CancelLoad();
        Display(new ReaderFile(Path.Combine(AppContext.BaseDirectory, "README.md"), Sample, false, Sample.Length),
            MarkdownView.Parse(Sample), null, sample: true);
    }

    private async void Reload(object sender, RoutedEventArgs e)
    {
        if (currentFile is { } file && !isSample) await OpenFile(file.Path);
    }

    private void RevealFile(object sender, RoutedEventArgs e)
    {
        if (currentFile is not { } file || isSample) return;
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

    private void GoHome(object sender, RoutedEventArgs e)
    {
        CancelLoad();
        currentFile = null;
        currentMarkdown = null;
        currentImages = new Dictionary<string, ImageSource>();
        searchIndex = null;
        outline = [];
        Preview.Document = null;
        Source.Clear();
        FindBox.Clear();
        searchDelay.Stop();
        matches.Clear();
        FindBar.Visibility = DocumentArea.Visibility = Visibility.Collapsed;
        Welcome.Visibility = Visibility.Visible;
        ReloadButton.IsEnabled = FindButton.IsEnabled = ReadButton.IsEnabled = SourceButton.IsEnabled = false;
        Caption.Text = "Home";
        Title = "The Readme";
        Status.Text = "Ready to read";
        RefreshRail();
        FadeIn(Welcome);
    }

    private void ShowRendered(object sender, RoutedEventArgs e) => SetMode(source: false);
    private void ShowSource(object sender, RoutedEventArgs e) => SetMode(source: true);
    private void SetMode(bool source)
    {
        if (currentFile is null || showingSource == source) return;
        showingSource = source;
        if (source && Source.Text.Length == 0) Source.Text = currentFile.Text;
        RefreshMode();
        if (FindBar.Visibility == Visibility.Visible) Search();
        FadeIn(source ? Source : Preview);
    }

    private void RefreshMode()
    {
        Preview.Visibility = showingSource ? Visibility.Collapsed : Visibility.Visible;
        Source.Visibility = showingSource ? Visibility.Visible : Visibility.Collapsed;
        ReadButton.Background = showingSource ? Brushes.Transparent : GetBrush("Hover");
        SourceButton.Background = showingSource ? GetBrush("Hover") : Brushes.Transparent;
    }

    private void ToggleRail(object sender, RoutedEventArgs e)
    {
        preferences.ShowSidebar = !preferences.ShowSidebar;
        preferences.Save();
        ShowRail();
        if (preferences.ShowSidebar) FadeIn(Rail);
    }

    private void ShowRail()
    {
        RailWidth.Width = new GridLength(preferences.ShowSidebar ? 219 : 0);
        Rail.Visibility = preferences.ShowSidebar ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        bool show = SettingsPane.Visibility != Visibility.Visible;
        SettingsPane.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            RefreshPath();
            FadeIn(SettingsPane);
            ThemeButtons.Children.OfType<Button>().First().Focus();
        }
        else SettingsButton.Focus();
    }

    private void OpenDefaultApps(object sender, RoutedEventArgs e)
    {
        try { WindowsIntegration.Open(WindowsIntegration.IsInstalled ? WindowsIntegration.DefaultAppsUri : "ms-settings:defaultapps"); }
        catch (Win32Exception ex) { Status.Text = ex.Message; }
    }

    private void RefreshPath()
    {
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
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindBox.Clear();
        searchDelay.Stop();
        Search();
        FindButton.Focus();
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        searchDelay.Stop();
        searchDelay.Start();
    }

    private void Search()
    {
        matches.Clear();
        matchIndex = 0;
        string query = FindBox.Text.Trim();
        if (query.Length == 0 || currentFile is null)
        {
            FindCount.Text = "";
            if (Preview.Selection is { } selection) selection.Select(selection.Start, selection.Start);
            Source.Select(Source.SelectionStart, 0);
            return;
        }
        string text = showingSource ? currentFile.Text : (searchIndex ??= new DocumentSearch(Preview.Document)).Text;
        int position = 0;
        while (matches.Count < 1000 && (position = text.IndexOf(query, position, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            matches.Add(position);
            position += query.Length;
        }
        SelectMatch();
    }

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
        int start = matches[matchIndex];
        int length = FindBox.Text.Trim().Length;
        FindCount.Text = $"{matchIndex + 1} of {matches.Count}{(matches.Count == 1000 ? "+" : "")}";
        if (showingSource)
        {
            Source.Select(start, length);
            int line = Source.GetLineIndexFromCharacterIndex(start);
            if (line >= 0) Source.ScrollToLine(line);
        }
        else if (searchIndex?.PointerAt(start, end: false) is { } first && searchIndex.PointerAt(start + length, end: true) is { } last)
        {
            Preview.Selection?.Select(first, last);
            first.Paragraph?.BringIntoView();
        }
    }

    private void JumpTo(string fragment)
    {
        if (outline.FirstOrDefault(item => item.Anchor.Equals(fragment, StringComparison.OrdinalIgnoreCase)) is { } heading)
            Dispatcher.InvokeAsync(() => heading.Target.BringIntoView(), DispatcherPriority.Loaded);
        else Status.Text = "Heading not found: " + fragment;
    }

    private async void OpenLink(string destination)
    {
        if (destination.StartsWith('#')) { JumpTo(Uri.UnescapeDataString(destination[1..])); return; }
        if (DocumentLinks.TryExternal(destination, out Uri? uri))
        {
            try { WindowsIntegration.Open(uri!.AbsoluteUri); }
            catch (Win32Exception ex) { Status.Text = ex.Message; }
            return;
        }
        if (!isSample && currentFile is { } file && DocumentLinks.LocalPath(file.Path, destination) is { } path && ReaderFile.Supports(path))
        {
            int hash = destination.IndexOf('#');
            await OpenFile(path, hash < 0 ? null : Uri.UnescapeDataString(destination[(hash + 1)..]));
        }
        else Status.Text = "This link points outside the supported document folder or uses an unsupported format.";
    }

    private void SetZoom(int value, bool save = true)
    {
        preferences.Zoom = Math.Clamp(value, 80, 160);
        Preview.Zoom = preferences.Zoom;
        Source.FontSize = 15.0 * preferences.Zoom / 100;
        ZoomLabel.Content = preferences.Zoom + "%";
        if (save) preferences.Save();
    }

    private void ZoomIn(object sender, RoutedEventArgs e) => SetZoom(preferences.Zoom + 10);
    private void ZoomOut(object sender, RoutedEventArgs e) => SetZoom(preferences.Zoom - 10);
    private void ZoomReset(object sender, RoutedEventArgs e) => SetZoom(100);

    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SettingsPane.IsVisible) ToggleSettings(sender, e);
            else if (FindBar.IsVisible) CloseFind(sender, e);
            else return;
        }
        else if (e.Key == Key.Enter && FindBox.IsKeyboardFocusWithin) MoveMatch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
        else if (e.Key == Key.F5) Reload(sender, e);
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            switch (e.Key)
            {
                case Key.O: PickFile(sender, e); break;
                case Key.F: OpenFind(sender, e); break;
                case Key.B: ToggleRail(sender, e); break;
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

    private static void FadeIn(UIElement element)
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast) return;
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(175))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void ClipHero(object sender, SizeChangedEventArgs e) =>
        ((Border)sender).Clip = new RectangleGeometry(new Rect(e.NewSize), 17, 17);
    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ToggleMaximize(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
