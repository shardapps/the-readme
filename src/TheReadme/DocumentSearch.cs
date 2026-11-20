using System.Text;
using System.Windows.Documents;

namespace TheReadme;

internal sealed class DocumentSearch
{
    private readonly record struct Segment(int Offset, int Length, TextPointer Position);
    private readonly List<Segment> segments = [];
    public string Text { get; }

    public DocumentSearch(FlowDocument document)
    {
        var text = new StringBuilder();
        TextPointer? cursor = document.ContentStart;
        while (cursor is not null && cursor.CompareTo(document.ContentEnd) < 0)
        {
            var context = cursor.GetPointerContext(LogicalDirection.Forward);
            if (context == TextPointerContext.Text)
            {
                string run = cursor.GetTextInRun(LogicalDirection.Forward);
                if (run.Length > 0)
                {
                    segments.Add(new(text.Length, run.Length, cursor));
                    text.Append(run);
                }
            }
            else if (context == TextPointerContext.ElementEnd && cursor.GetAdjacentElement(LogicalDirection.Forward) is Paragraph or LineBreak)
            {
                text.Append('\n');
            }
            cursor = cursor.GetNextContextPosition(LogicalDirection.Forward);
        }
        Text = text.ToString();
    }

    public TextPointer? PointerAt(int offset, bool end)
    {
        int low = 0;
        int high = segments.Count - 1;
        int target = end ? offset - 1 : offset;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            var segment = segments[middle];
            if (target < segment.Offset) high = middle - 1;
            else if (target >= segment.Offset + segment.Length) low = middle + 1;
            else return segment.Position.GetPositionAtOffset(offset - segment.Offset);
        }
        return null;
    }
}
