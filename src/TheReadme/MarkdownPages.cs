using System.Text;
using System.Windows.Media;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace TheReadme;

internal sealed class MarkdownPages
{
    internal sealed record Heading(string Label, int Level, string Anchor, int Page);
    private readonly List<(int Start, int Count)> ranges = [];
    private readonly List<Heading> headings = [];
    private readonly MarkdownDocument document;
    private readonly IReadOnlyDictionary<string, ImageSource> images;

    public MarkdownPages(MarkdownDocument document, IReadOnlyDictionary<string, ImageSource> images)
    {
        this.document = document;
        this.images = images;
        var anchors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int start = 0;
        int weight = 0;
        for (int index = 0; index < document.Count; index++)
        {
            Block block = document[index];
            int cost = Math.Max(180, block.Span.Length);
            if (index > start && (long)weight + cost > 3200)
            {
                ranges.Add((start, index - start));
                start = index;
                weight = 0;
            }
            weight += cost;
            if (block is HeadingBlock heading) AddHeading(heading);
            else if (block is ContainerBlock nested)
                foreach (var included in nested.Descendants<HeadingBlock>()) AddHeading(included);
        }
        ranges.Add((start, document.Count - start));

        void AddHeading(HeadingBlock heading)
        {
            string label = InlineText(heading.Inline).Trim();
            string anchor = heading.GetAttributes().Id ?? label;
            if (label.Length > 0 && anchors.Add(anchor)) headings.Add(new(label, heading.Level, anchor, ranges.Count));
        }
    }

    internal int Count => ranges.Count;
    internal IReadOnlyList<Heading> Headings => headings;

    internal IEnumerable<Block> Blocks(int page)
    {
        var range = ranges[page];
        for (int index = range.Start; index < range.Start + range.Count; index++) yield return document[index];
    }

    internal string SearchText(int page)
    {
        var result = new StringBuilder();
        AppendBlocks(Blocks(page), result, 0);
        return result.ToString();
    }

    private void AppendBlocks(IEnumerable<Block> blocks, StringBuilder result, int depth)
    {
        if (depth > 32) return;
        foreach (Block block in blocks)
            switch (block)
            {
                case ParagraphBlock paragraph:
                    AppendInline(paragraph.Inline, result, 0);
                    result.Append('\n');
                    break;
                case HeadingBlock heading:
                    AppendInline(heading.Inline, result, 0);
                    result.Append('\n');
                    break;
                case CodeBlock code:
                    for (int i = 0; i < code.Lines.Count; i++) result.Append(code.Lines.Lines[i].Slice.ToString()).Append('\n');
                    if (code.Lines.Count == 0) result.Append('\n');
                    break;
                case Table table:
                    foreach (var row in table.OfType<TableRow>())
                        foreach (var cell in row.OfType<TableCell>().Take(64))
                            AppendBlocks(cell, result, depth + 1);
                    break;
                case ThematicBreakBlock:
                    result.Append('\n');
                    break;
                case ContainerBlock nested:
                    AppendBlocks(nested, result, depth + 1);
                    break;
            }
    }

    private string InlineText(ContainerInline? source)
    {
        var result = new StringBuilder();
        AppendInline(source, result, 0);
        return result.ToString();
    }

    private void AppendInline(ContainerInline? source, StringBuilder result, int depth)
    {
        if (source is null || depth > 32) return;
        foreach (Inline inline in source)
            switch (inline)
            {
                case LiteralInline literal: result.Append(literal.Content.ToString()); break;
                case HtmlEntityInline entity: result.Append(entity.Transcoded.ToString()); break;
                case AutolinkInline auto: result.Append(auto.Url); break;
                case CodeInline code: result.Append(code.Content); break;
                case LineBreakInline line: result.Append(line.IsHard ? '\n' : ' '); break;
                case TaskList task: result.Append(task.Checked ? "☑ " : "☐ "); break;
                case LinkInline image when image.IsImage:
                    if (image.Url is null || !images.ContainsKey(image.Url))
                    {
                        var alt = new StringBuilder();
                        AppendInline(image, alt, depth + 1);
                        string label = alt.ToString().Trim();
                        result.Append('[').Append(label.Length == 0 ? "Image" : label).Append(']');
                    }
                    break;
                case ContainerInline nested: AppendInline(nested, result, depth + 1); break;
            }
    }
}
