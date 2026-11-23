using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Markdig.Syntax;

namespace TheReadme;

public partial class MainWindow
{
    private ViewMode PreferredView => (preferences.OpeningView == MarkdownOpeningView.LastUsed
        ? preferences.LastMarkdownView : preferences.OpeningView) switch
        {
            MarkdownOpeningView.Edit => ViewMode.Edit,
            MarkdownOpeningView.Split => ViewMode.Split,
            _ => ViewMode.Read
        };

    private async Task ChooseView(ViewMode requested)
    {
        if (currentFile is not { Format.Kind: FileKind.Markdown } file) return;
        preferences.LastMarkdownView = (MarkdownOpeningView)requested;
        preferences.Save();
        if (requested == ViewMode.Edit)
            await OpenEditorAtCaret();
        else
        {
            await SetMode(requested);
            if (!ReferenceEquals(file, currentFile) || requested != mode) return;
            if (requested == ViewMode.Read) Preview.Focus();
            else Source.Focus();
        }
    }

    private void ChangeOpeningView(object sender, SelectionChangedEventArgs e)
    {
        if (OpeningViewBox.SelectedItem is not ComboBoxItem { Tag: string value } ||
            !Enum.TryParse(value, out MarkdownOpeningView view) || preferences.OpeningView == view) return;
        preferences.OpeningView = view;
        preferences.Save();
    }

    private async void PreviewTyped(object sender, TextCompositionEventArgs e)
    {
        if (mode != ViewMode.Read || saving || !preferences.TypeToEdit || SettingsPane.IsVisible ||
            string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
            return;

        e.Handled = true;
        await OpenEditorAtCaret();
        if (Source.IsReadOnly) return;
        int start = Source.SelectionStart;
        ApplyEdit(new(start, Source.SelectionLength, e.Text, start + e.Text.Length, 0));
    }

    private async Task OpenEditorAtCaret()
    {
        if (mode == ViewMode.Read && currentFile is { Format.Kind: FileKind.Markdown } file)
        {
            string source = sourceLoaded ? Source.Text : file.Text;
            int start = sourceLoaded ? Source.CaretIndex : 0;
            int length = 0;
            if (source == renderedText && Preview.Selection is { } selection)
            {
                var first = SourcePosition(selection.Start, source, LogicalDirection.Forward);
                var last = SourcePosition(selection.End, source, LogicalDirection.Backward);
                if (first is { } left) start = left.Position;
                if (!selection.IsEmpty && first is { Run: not null } a && last is { } b && a.Run == b.Run)
                    length = Math.Max(0, b.Position - start);
            }
            await SetMode(ViewMode.Edit);
            Source.Select(start, length);
            int line = Source.GetLineIndexFromCharacterIndex(start);
            if (line >= 0) Source.ScrollToLine(line);
        }
        else await SetMode(ViewMode.Edit);
        Source.Focus();
    }

    private static (int Position, Run? Run)? SourcePosition(TextPointer pointer, string source, LogicalDirection direction)
    {
        var insertion = pointer.GetInsertionPosition(direction);
        if (insertion.Parent is Run { Tag: SourceSpan span } run && span.Start >= 0 && span.End < source.Length)
        {
            var original = source.AsSpan(span.Start, span.Length);
            int offset = original.IndexOf(run.Text.AsSpan(), StringComparison.Ordinal);
            if (offset >= 0)
            {
                int index = Math.Clamp(run.ContentStart.GetOffsetToPosition(insertion), 0, run.Text.Length);
                return (span.Start + offset + index, run);
            }
            return (direction == LogicalDirection.Forward ? span.Start : span.End + 1, null);
        }
        if (pointer.Paragraph?.Tag is SourceSpan block)
            return (Math.Clamp(block.Start, 0, source.Length), null);
        return null;
    }

    private async void OpenReplace(object sender, RoutedEventArgs e)
    {
        if (currentFile is null || saving) return;
        if (mode == ViewMode.Read) await OpenEditorAtCaret();
        OpenFind(sender, e);
        ReplaceRow.Visibility = Visibility.Visible;
        FadeIn(ReplaceRow, travel: 3);
    }

    private void ReplaceNext(object sender, RoutedEventArgs e) => ReplaceMatches(all: false);
    private void ReplaceAll(object sender, RoutedEventArgs e) => ReplaceMatches(all: true);

    private void ReplaceMatches(bool all)
    {
        string query = FindBox.Text;
        if (mode == ViewMode.Read || currentFile is null || Source.IsReadOnly || query.Length == 0) return;
        string text = Source.Text;
        string replacement = ReplaceBox.Text;
        var comparison = SearchComparison;
        int count = 0;
        if (all)
        {
            for (int offset = 0; (offset = text.IndexOf(query, offset, comparison)) >= 0; offset += query.Length)
                count++;
            if ((long)text.Length + (long)count * (replacement.Length - query.Length) > ReaderFile.MaxBytes)
            {
                Status.Text = "Replacement would exceed the document size limit.";
                return;
            }
            if (count > 0) ApplyEdit(new(0, text.Length, text.Replace(query, replacement, comparison), 0, 0));
            searchDelay.Stop();
            Search();
        }
        else
        {
            int start = text.IndexOf(query, Source.SelectionStart, comparison);
            if (start < 0) start = text.IndexOf(query, comparison);
            if (start >= 0)
            {
                if (text.Length - query.Length + replacement.Length > ReaderFile.MaxBytes)
                {
                    Status.Text = "Replacement would exceed the document size limit.";
                    return;
                }
                int next = start + replacement.Length;
                ApplyEdit(new(start, query.Length, replacement, next, 0));
                count = 1;
                searchDelay.Stop();
                Search(next);
            }
        }
        Status.Text = count == 0 ? "No matches to replace." : $"Replaced {count} {(count == 1 ? "match" : "matches")}.";
        ReplaceBox.Focus();
    }
}
