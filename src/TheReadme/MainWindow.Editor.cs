using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TheReadme;

public partial class MainWindow
{
    private enum ViewMode { Read, Edit, Split }

    private const string CodeSample = """"
        """A small source file to practice searching and jumping to a line."""


        def reading_time(words: int, words_per_minute: int = 220) -> int:
            """Return a whole-minute reading estimate."""
            return max(1, (words + words_per_minute - 1) // words_per_minute)


        for size in (160, 450, 880):
            print(f"{size} words: {reading_time(size)} minute(s)")
        """";

    private readonly DispatcherTimer previewDelay = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private ViewMode mode;
    private CancellationTokenSource? previewWork;
    private string savedText = "";
    private string? renderedText;
    private bool sourceLoaded;
    private bool settingSource;
    private bool dirty;
    private bool saving;
    private bool closePromptPending;
    private bool allowClosing;
    internal Func<MessageBoxResult>? ConfirmUnsavedForTest { get; set; }

    private void LoadEditor(string text, bool loadNow)
    {
        settingSource = true;
        try
        {
            Source.IsUndoEnabled = false;
            Source.Text = loadNow ? text : "";
            Source.Select(0, 0);
            Source.ScrollToHome();
            Source.IsUndoEnabled = true;
            sourceLoaded = loadNow;
        }
        finally { settingSource = false; }
    }

    private void SourceChanged(object sender, TextChangedEventArgs e)
    {
        if (settingSource || currentFile?.Format.CanEdit != true) return;
        CancelLoad();
        CancelPreview();
        dirty = !string.Equals(Source.Text, savedText, StringComparison.Ordinal);
        RefreshDocumentLabel();
        Status.Text = dirty ? currentFile.Format.Label + " · Unsaved changes" : FileStatus(currentFile);
        if (mode == ViewMode.Split) previewDelay.Start();
        if (FindBar.IsVisible) SearchChanged(sender, e);
    }

    private async void NewDocument(object sender, RoutedEventArgs e)
    {
        if (!await CanLeaveDocument()) return;
        CancelLoad();
        Display(ReaderFile.Untitled("Untitled.md"), null, null, edit: true);
        Source.Focus();
    }

    private async void OpenCodeSample(object sender, RoutedEventArgs e)
    {
        if (!await CanLeaveDocument()) return;
        CancelLoad();
        Display(ReaderFile.Untitled("reading_time.py", CodeSample), null, null, sample: true);
    }

    private async void ShowRendered(object sender, RoutedEventArgs e) => await SetMode(ViewMode.Read);
    private async void ShowSource(object sender, RoutedEventArgs e) => await SetMode(ViewMode.Edit);
    private async void ShowSplit(object sender, RoutedEventArgs e) => await SetMode(ViewMode.Split);

    private async Task SetMode(ViewMode requested)
    {
        if (currentFile is null || mode == requested || (requested != ViewMode.Edit && currentFile.Format.Kind != FileKind.Markdown))
            return;
        mode = requested;
        if (mode != ViewMode.Read && !sourceLoaded) LoadEditor(currentFile.Text, loadNow: true);
        LineEntry.Visibility = Visibility.Collapsed;
        RefreshMode();
        if (mode != ViewMode.Edit) await RefreshDraftPreview();
        if (mode == requested && FindBar.IsVisible) Search();
        FadeIn(mode == ViewMode.Read ? Preview : Source);
    }

    private void RefreshMode()
    {
        bool markdown = currentFile?.Format.Kind == FileKind.Markdown;
        bool canEdit = currentFile?.Format.CanEdit == true;
        ViewModes.Visibility = markdown ? Visibility.Visible : Visibility.Collapsed;
        ReadButton.IsEnabled = SourceButton.IsEnabled = SplitButton.IsEnabled = markdown;
        Preview.Visibility = markdown && mode != ViewMode.Edit ? Visibility.Visible : Visibility.Collapsed;
        Source.Visibility = currentFile is not null && mode != ViewMode.Read ? Visibility.Visible : Visibility.Collapsed;
        EditorTools.Visibility = Source.Visibility;
        FormattingTools.Visibility = markdown ? Visibility.Visible : Visibility.Collapsed;
        Source.IsReadOnly = !canEdit || saving;
        SourceKind.Text = canEdit ? (markdown ? "" : "Plain text") : currentFile is { } file ? file.Format.Label + " · Read only" : "";
        AutomationProperties.SetName(Source, canEdit ? "Document editor" : "Read-only source");
        ReadButton.Background = mode == ViewMode.Read ? GetBrush("Hover") : Brushes.Transparent;
        SourceButton.Background = mode == ViewMode.Edit ? GetBrush("Hover") : Brushes.Transparent;
        SplitButton.Background = mode == ViewMode.Split ? GetBrush("Hover") : Brushes.Transparent;
        UpdateWrap();
        ArrangeSurface();
        RefreshDocumentLabel();
    }

    private void DocumentResized(object sender, SizeChangedEventArgs e) => ArrangeSurface();

    private void ArrangeSurface()
    {
        bool split = currentFile?.Format.Kind == FileKind.Markdown && mode == ViewMode.Split;
        bool stacked = split && DocumentSurface.ActualWidth < 800;
        bool sideBySide = split && !stacked;
        EditorColumn.Width = sideBySide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        EditorRow.Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        SplitColumn.Width = new GridLength(sideBySide ? 1 : 0);
        SplitRow.Height = new GridLength(stacked ? 1 : 0);
        Grid.SetColumn(Source, sideBySide ? 0 : 2);
        Grid.SetRow(Source, stacked ? 0 : 2);
        Grid.SetColumn(EditorDivider, sideBySide ? 1 : 2);
        Grid.SetRow(EditorDivider, stacked ? 1 : 2);
        EditorDivider.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        Preview.Margin = split || DocumentSurface.ActualWidth < 900 ? new Thickness(16, 9, 12, 0) : new Thickness(32, 10, 24, 0);
    }

    private async Task RefreshDraftPreview()
    {
        if (currentFile is not { Format.Kind: FileKind.Markdown } file || !sourceLoaded || renderedText == Source.Text) return;
        CancelPreview();
        var work = new CancellationTokenSource();
        previewWork = work;
        string draft = Source.Text;
        try
        {
            var result = await Task.Run(async () =>
            {
                var token = work.Token;
                await readerGate.WaitAsync(token);
                try
                {
                    var markdown = MarkdownView.Parse(draft);
                    token.ThrowIfCancellationRequested();
                    var images = MarkdownView.LoadImages(file.IsOnDisk ? markdown : null, file, token);
                    return (markdown, images);
                }
                finally { readerGate.Release(); }
            }, work.Token);
            if (previewWork != work || !ReferenceEquals(file, currentFile)) return;
            currentMarkdown = result.markdown;
            currentImages = result.images;
            renderedText = draft;
            double offset = FindScrollViewer(Preview)?.VerticalOffset ?? 0;
            RenderPreview();
            if (offset > 0) FindScrollViewer(Preview)?.ScrollToVerticalOffset(offset);
            if (mode == ViewMode.Read && FindBar.IsVisible) Search();
        }
        catch (OperationCanceledException) when (work.IsCancellationRequested) { }
        finally
        {
            if (previewWork == work) previewWork = null;
            work.Dispose();
        }
    }

    private void CancelPreview()
    {
        previewDelay.Stop();
        previewWork?.Cancel();
        previewWork = null;
    }

    private void RefreshDocumentLabel()
    {
        if (currentFile is not { } file) return;
        Caption.Text = file.Name + (dirty ? "  ●" : "");
        Title = file.Name + (dirty ? " *" : "") + " · The Readme";
        SaveButton.Visibility = SaveAsButton.Visibility = file.Format.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = !saving && (dirty || !file.IsOnDisk);
        SaveAsButton.IsEnabled = !saving;
    }

    private static string FileStatus(ReaderFile file) => $"{file.Format.Label} · {file.Bytes / 1024.0:0.#} KB";

    private async void SaveClicked(object sender, RoutedEventArgs e) => await SaveCurrent(saveAs: false);
    private async void SaveAsClicked(object sender, RoutedEventArgs e) => await SaveCurrent(saveAs: true);

    private async Task<bool> SaveCurrent(bool saveAs)
    {
        if (currentFile is not { Format.CanEdit: true } file || saving) return false;
        if (!saveAs && file.IsOnDisk)
            return !dirty || await SaveDocument(file.Path);

        CancelLoad();
        var dialog = new SaveFileDialog
        {
            Title = "Save your document",
            FileName = Path.GetFileNameWithoutExtension(file.Name),
            InitialDirectory = file.IsOnDisk ? Path.GetDirectoryName(file.Path) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = "Markdown (*.md)|*.md|Plain text (*.txt)|*.txt",
            FilterIndex = file.Format.Kind == FileKind.PlainText ? 2 : 1,
            AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog(this) == true && ReferenceEquals(file, currentFile) &&
            await SaveDocument(dialog.FileName, File.Exists(dialog.FileName));
    }

    internal async Task<bool> SaveDocument(string destination, bool overwrite = false)
    {
        if (currentFile is not { Format.CanEdit: true } file || saving) return false;
        saving = true;
        CancelLoad();
        string draft = sourceLoaded ? Source.Text : file.Text;
        Source.IsReadOnly = true;
        RefreshDocumentLabel();
        Status.Text = "Saving " + Path.GetFileName(destination) + "…";
        try
        {
            ReaderFile saved = await Task.Run(() => file.SaveAsync(destination, draft, overwrite));
            CancelPreview();
            currentFile = saved;
            savedText = draft;
            dirty = false;
            preferences.Record(saved.Path);
            FileTitle.Text = saved.Name;
            FileLocation.Text = saved.Path;
            FileLocation.ToolTip = saved.Path;
            RevealButton.IsEnabled = ReloadButton.IsEnabled = true;
            Status.Text = "Saved · " + FileStatus(saved);
            if (saved.Format.Kind != FileKind.Markdown)
            {
                mode = ViewMode.Edit;
                if (!sourceLoaded) LoadEditor(draft, loadNow: true);
                currentMarkdown = null;
                currentImages = new Dictionary<string, ImageSource>();
                RenderPreview();
            }
            else if (!string.Equals(file.Path, saved.Path, StringComparison.OrdinalIgnoreCase) || renderedText != draft)
            {
                if (!sourceLoaded) LoadEditor(draft, loadNow: true);
                renderedText = null;
                await RefreshDraftPreview();
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            Status.Text = "Could not save. " + ex.Message;
            return false;
        }
        finally
        {
            saving = false;
            RefreshMode();
        }
    }

    private async Task<bool> CanLeaveDocument()
    {
        if (saving) return false;
        if (!dirty) return true;
        MessageBoxResult choice = ConfirmUnsavedForTest?.Invoke() ?? MessageBox.Show(this,
            $"Save changes to {currentFile!.Name} before leaving?", "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return choice == MessageBoxResult.No || (choice == MessageBoxResult.Yes && await SaveCurrent(saveAs: false));
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClosing && (dirty || saving))
        {
            e.Cancel = true;
            if (!saving && !closePromptPending)
            {
                closePromptPending = true;
                Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (!await CanLeaveDocument()) return;
                        allowClosing = true;
                        Close();
                    }
                    finally { closePromptPending = false; }
                });
            }
        }
        base.OnClosing(e);
    }

    private void FormatInline(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string marker }) ApplyInline(marker);
    }

    private void ApplyInline(string marker)
    {
        if (currentFile?.Format.Kind != FileKind.Markdown || mode == ViewMode.Read || Source.IsReadOnly) return;
        string placeholder = marker switch { "**" => "bold text", "*" => "italic text", "~~" => "text", _ => "code" };
        ApplyEdit(MarkdownEdits.Inline(Source.Text, Source.SelectionStart, Source.SelectionLength, marker, placeholder));
    }

    private void OpenHeadingMenu(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void ChooseHeading(object sender, RoutedEventArgs e)
    {
        if (currentFile?.Format.Kind == FileKind.Markdown && !Source.IsReadOnly && sender is MenuItem { Tag: string level } &&
            int.TryParse(level, out int size))
            ApplyEdit(MarkdownEdits.Heading(Source.Text, Source.SelectionStart, Source.SelectionLength, size));
    }

    private void ApplyEdit(MarkupEdit edit)
    {
        if (Source.Text.Length - edit.Length + edit.Text.Length > ReaderFile.MaxBytes) return;
        Source.Select(edit.Start, edit.Length);
        Source.SelectedText = edit.Text;
        Source.Focus();
        Source.Select(edit.SelectionStart, edit.SelectionLength);
    }

    private void ToggleWrap(object sender, RoutedEventArgs e)
    {
        if (currentFile?.Format.Kind == FileKind.Source) preferences.WrapCode = !preferences.WrapCode;
        else preferences.WrapText = !preferences.WrapText;
        preferences.Save();
        UpdateWrap();
    }

    private void UpdateWrap()
    {
        bool wrap = currentFile?.Format.Kind == FileKind.Source ? preferences.WrapCode : preferences.WrapText;
        Source.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Source.HorizontalScrollBarVisibility = wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        WrapButton.Background = wrap ? GetBrush("Hover") : Brushes.Transparent;
        AutomationProperties.SetItemStatus(WrapButton, wrap ? "On" : "Off");
    }

    private async void OpenGoTo(object sender, RoutedEventArgs e)
    {
        if (currentFile is null) return;
        if (mode == ViewMode.Read) await SetMode(ViewMode.Edit);
        LineEntry.Visibility = Visibility.Visible;
        LineBox.Focus();
        LineBox.SelectAll();
    }

    private void GoToClicked(object sender, RoutedEventArgs e) => GoToLine();

    private void GoToLine()
    {
        int index = int.TryParse(LineBox.Text, out int line) ? MarkdownEdits.OffsetOfLine(Source.Text, line) : -1;
        if (index < 0)
        {
            Status.Text = "Enter a line number in this file.";
            return;
        }
        LineEntry.Visibility = Visibility.Collapsed;
        Source.Focus();
        Source.Select(index, 0);
        int visualLine = Source.GetLineIndexFromCharacterIndex(index);
        if (visualLine >= 0) Source.ScrollToLine(visualLine);
        Status.Text = $"Line {line}";
    }
}
