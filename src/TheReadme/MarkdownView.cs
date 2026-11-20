using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Wpf = System.Windows.Documents;

namespace TheReadme;

public sealed record OutlineItem(string Label, int Level, Wpf.Paragraph Target, string Anchor);

public sealed class MarkdownView(FrameworkElement palette, ReaderFile file, IReadOnlyDictionary<string, ImageSource> images, Action<string> openLink)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub).UsePipeTables().UseTaskLists().UseAutoLinks().UseEmphasisExtras().DisableHtml().Build();
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    private readonly List<OutlineItem> outline = [];
    private readonly HashSet<string> anchors = new(StringComparer.OrdinalIgnoreCase);
    private int nodeCount;
    private int renderedImages;

    public IReadOnlyList<OutlineItem> Outline => outline;
    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);

    public static IReadOnlyDictionary<string, ImageSource> LoadImages(MarkdownDocument? document, ReaderFile file, CancellationToken token)
    {
        var result = new Dictionary<string, ImageSource>(StringComparer.Ordinal);
        if (document is null) return result;
        foreach (var image in document.Descendants<LinkInline>().Where(link => link.IsImage && link.Url is not null).Take(24))
        {
            token.ThrowIfCancellationRequested();
            string url = image.Url!;
            if (!result.ContainsKey(url) && LoadImage(file.Path, url) is { } bitmap) result.Add(url, bitmap);
            if (result.Count >= 12) break;
        }
        return result;
    }

    public FlowDocument Render(MarkdownDocument? markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 17,
            LineHeight = 27,
            TextAlignment = TextAlignment.Left,
            ColumnWidth = double.PositiveInfinity,
            PagePadding = new Thickness(6, 12, 16, 56),
            Foreground = Brush("Ink"),
            IsOptimalParagraphEnabled = false
        };
        if (markdown is null)
        {
            document.Blocks.Add(new Wpf.Paragraph(new Run(file.Text)) { FontFamily = Mono, FontSize = 15 });
            return document;
        }
        WriteBlocks(markdown, document.Blocks, 0);
        if (nodeCount > 18000)
            document.Blocks.Add(new Wpf.Paragraph(new Run("Preview limit reached. Open Source to read the full file.")) { Foreground = Brush("Muted") });
        return document;
    }

    private void WriteBlocks(ContainerBlock source, BlockCollection destination, int depth)
    {
        if (depth > 32) return;
        foreach (var block in source)
        {
            if (++nodeCount > 18000) break;
            switch (block)
            {
                case HeadingBlock heading:
                    var title = Paragraph(heading.Inline);
                    title.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
                    title.FontWeight = FontWeights.SemiBold;
                    title.FontSize = heading.Level switch { 1 => 39, 2 => 27, 3 => 22, _ => 18 };
                    title.LineHeight = double.NaN;
                    title.Margin = new Thickness(0, destination.Count == 0 ? 0 : 21, 0, 12);
                    string label = new TextRange(title.ContentStart, title.ContentEnd).Text.Trim();
                    string anchor = heading.GetAttributes().Id ?? label;
                    if (label.Length > 0 && anchors.Add(anchor)) outline.Add(new(label, heading.Level, title, anchor));
                    destination.Add(title);
                    break;
                case ParagraphBlock paragraph:
                    destination.Add(Paragraph(paragraph.Inline));
                    break;
                case CodeBlock code:
                    destination.Add(new Wpf.Paragraph(new Run(code.Lines.ToString()))
                    {
                        FontFamily = Mono, FontSize = 14, LineHeight = 22, Background = Brush("Soft"),
                        Padding = new Thickness(16, 11, 16, 11), Margin = new Thickness(0, 4, 0, 14)
                    });
                    break;
                case QuoteBlock quote:
                    var section = new Section
                    {
                        Background = Brush("Soft"), BorderBrush = Brush("Accent"), BorderThickness = new Thickness(3, 0, 0, 0),
                        Padding = new Thickness(15, 10, 13, 0), Margin = new Thickness(0, 3, 0, 14)
                    };
                    WriteBlocks(quote, section.Blocks, depth + 1);
                    destination.Add(section);
                    break;
                case ListBlock list:
                    var newList = new Wpf.List
                    {
                        MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Padding = new Thickness(25, 0, 0, 0), Margin = new Thickness(0, 0, 0, 8)
                    };
                    if (list.IsOrdered && int.TryParse(list.OrderedStart, out int start) && start > 0) newList.StartIndex = start;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        if (++nodeCount > 18000) break;
                        var newItem = new ListItem();
                        WriteBlocks(item, newItem.Blocks, depth + 1);
                        foreach (var child in newItem.Blocks.OfType<Wpf.Paragraph>()) child.Margin = new Thickness(0, 0, 0, 4);
                        newList.ListItems.Add(newItem);
                    }
                    destination.Add(newList);
                    break;
                case Markdig.Extensions.Tables.Table table:
                    destination.Add(RenderTable(table, depth));
                    break;
                case ThematicBreakBlock:
                    destination.Add(new Wpf.Paragraph
                    {
                        BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brush("Line"),
                        FontSize = 1, LineHeight = 1, Margin = new Thickness(0, 8, 0, 16)
                    });
                    break;
            }
        }
    }

    private Wpf.Table RenderTable(Markdig.Extensions.Tables.Table table, int depth)
    {
        var result = new Wpf.Table { CellSpacing = 0, Margin = new Thickness(0, 4, 0, 16) };
        for (int column = 0; column < Math.Min(table.ColumnDefinitions.Count, 64); column++) result.Columns.Add(new TableColumn());
        var rows = new TableRowGroup();
        result.RowGroups.Add(rows);
        int rowIndex = 0;
        foreach (var row in table.OfType<Markdig.Extensions.Tables.TableRow>())
        {
            if (++nodeCount > 18000) break;
            var rendered = new Wpf.TableRow { Background = row.IsHeader || rowIndex++ % 2 == 1 ? Brush("Soft") : null };
            foreach (var cell in row.OfType<Markdig.Extensions.Tables.TableCell>().Take(64))
            {
                if (++nodeCount > 18000) break;
                var renderedCell = new Wpf.TableCell
                {
                    Padding = new Thickness(9, 6, 9, 6), BorderThickness = new Thickness(0, 0, 1, 1), BorderBrush = Brush("Line"),
                    FontSize = 14, LineHeight = 21, FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal
                };
                WriteBlocks(cell, renderedCell.Blocks, depth + 1);
                foreach (var paragraph in renderedCell.Blocks.OfType<Wpf.Paragraph>()) paragraph.Margin = new Thickness(0);
                rendered.Cells.Add(renderedCell);
            }
            rows.Rows.Add(rendered);
        }
        return result;
    }

    private Wpf.Paragraph Paragraph(ContainerInline? source)
    {
        var result = new Wpf.Paragraph { Margin = new Thickness(0, 0, 0, 13) };
        if (source is not null) WriteInlines(source, result.Inlines, 0, false);
        return result;
    }

    private void WriteInlines(ContainerInline source, InlineCollection destination, int depth, bool insideLink)
    {
        if (depth > 32) return;
        foreach (var inline in source)
        {
            if (++nodeCount > 18000) break;
            switch (inline)
            {
                case LiteralInline literal: destination.Add(new Run(literal.Content.ToString())); break;
                case HtmlEntityInline entity: destination.Add(new Run(entity.Transcoded.ToString())); break;
                case AutolinkInline auto:
                    var link = MakeLink(auto.IsEmail ? "mailto:" + auto.Url : auto.Url);
                    link.Inlines.Add(new Run(auto.Url));
                    destination.Add(link);
                    break;
                case CodeInline code:
                    destination.Add(new Run(code.Content) { FontFamily = Mono, Background = Brush("Soft") });
                    break;
                case LineBreakInline line:
                    if (line.IsHard) destination.Add(new LineBreak());
                    else destination.Add(new Run(" "));
                    break;
                case TaskList task:
                    destination.Add(new Run(task.Checked ? "☑ " : "☐ ") { Foreground = Brush("Accent") });
                    break;
                case LinkInline image when image.IsImage:
                    var alt = new Span();
                    WriteInlines(image, alt.Inlines, depth + 1, true);
                    string alternate = new TextRange(alt.ContentStart, alt.ContentEnd).Text.Trim();
                    if (TryImage(image.Url, alternate) is { } localImage) destination.Add(localImage);
                    else destination.Add(new Run(alternate.Length == 0 ? "[Image]" : $"[{alternate}]") { Foreground = Brush("Muted") });
                    break;
                case LinkInline linkInline when !insideLink:
                    var hyperlink = MakeLink(linkInline.GetDynamicUrl?.Invoke() ?? linkInline.Url ?? "");
                    WriteInlines(linkInline, hyperlink.Inlines, depth + 1, true);
                    destination.Add(hyperlink);
                    break;
                case EmphasisInline emphasis:
                    var formatted = new Span();
                    if (emphasis.DelimiterChar == '~') formatted.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount == 2) formatted.FontWeight = FontWeights.Bold;
                    else formatted.FontStyle = FontStyles.Italic;
                    WriteInlines(emphasis, formatted.Inlines, depth + 1, insideLink);
                    destination.Add(formatted);
                    break;
                case ContainerInline container:
                    WriteInlines(container, destination, depth + 1, insideLink);
                    break;
            }
        }
    }

    private Hyperlink MakeLink(string url)
    {
        var hyperlink = new Hyperlink { Foreground = Brush("Accent"), ToolTip = url.Length > 300 ? url[..300] + "…" : url };
        hyperlink.Click += (_, _) => openLink(url);
        return hyperlink;
    }

    private InlineUIContainer? TryImage(string? url, string alt)
    {
        if (renderedImages >= 24 || url is null || !images.TryGetValue(url, out var bitmap)) return null;
        renderedImages++;
        var control = new System.Windows.Controls.Image
        {
            Source = bitmap, MaxWidth = 660, MaxHeight = 420, Stretch = Stretch.Uniform,
            ToolTip = alt.Length == 0 ? "Local image" : alt
        };
        System.Windows.Automation.AutomationProperties.SetName(control, (string)control.ToolTip);
        return new InlineUIContainer(control);
    }

    private static BitmapImage? LoadImage(string documentPath, string url)
    {
        if (DocumentLinks.LocalPath(documentPath, url) is not { } path) return null;
        if (Path.GetExtension(path).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")) return null;
        try
        {
            if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) return null;
            // Verify every parent, including the document directory's ancestors.
            for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 2 * 1024 * 1024) return null;
            byte[] header = new byte[8];
            if (stream.ReadAtLeast(header, header.Length, false) != header.Length || !IsSupportedBitmap(header)) return null;
            stream.Position = 0;
            var meta = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            if ((long)meta.PixelWidth * meta.PixelHeight > 12_000_000 || meta.PixelWidth > 8192 || meta.PixelHeight > 8192)
                return null;
            stream.Position = 0;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;
            if (meta.PixelWidth * 420L >= meta.PixelHeight * 660L) bitmap.DecodePixelWidth = Math.Min(meta.PixelWidth, 660);
            else bitmap.DecodePixelHeight = Math.Min(meta.PixelHeight, 420);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FileFormatException)
        {
            return null;
        }
    }

    private static bool IsSupportedBitmap(ReadOnlySpan<byte> header) =>
        header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
        header.StartsWith(new byte[] { 255, 216, 255 }) ||
        header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8) || header.StartsWith("BM"u8);

    private Brush Brush(string key) => (Brush)palette.FindResource(key);
}
