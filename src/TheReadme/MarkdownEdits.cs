namespace TheReadme;

internal readonly record struct MarkupEdit(int Start, int Length, string Text, int SelectionStart, int SelectionLength);

internal static class MarkdownEdits
{
    public static MarkupEdit Inline(string document, int start, int length, string marker, string placeholder)
    {
        string selected = length == 0 ? placeholder : document.Substring(start, length);
        if (length > 0 && HasOuterMarkers(document, start, start + length, marker))
            return new(start - marker.Length, length + 2 * marker.Length, selected, start - marker.Length, length);

        string trimmed = selected.Trim(' ', '\t');
        if (length > 0 && trimmed.Length == 0) return new(start, length, selected, start, length);
        int prefix = selected.Length - selected.TrimStart(' ', '\t').Length;
        int leading = 0;
        while (leading < trimmed.Length && trimmed[leading] == marker[0]) leading++;
        int trailing = 0;
        while (trailing < trimmed.Length - leading && trimmed[trimmed.Length - trailing - 1] == marker[0]) trailing++;
        if (CanToggle(marker, leading, trailing))
        {
            string inner = trimmed.Substring(marker.Length, trimmed.Length - 2 * marker.Length);
            string replacement = selected.Substring(0, prefix) + inner + selected.Substring(prefix + trimmed.Length);
            return new(start, length, replacement, start + prefix, inner.Length);
        }

        string result = selected.Substring(0, prefix) + marker + trimmed + marker + selected.Substring(prefix + trimmed.Length);
        return new(start, length, result, start + prefix + marker.Length, trimmed.Length);
    }

    private static bool HasOuterMarkers(string text, int from, int to, string marker)
    {
        int left = from;
        while (left > 0 && text[left - 1] == marker[0]) left--;
        int right = to;
        while (right < text.Length && text[right] == marker[0]) right++;
        return CanToggle(marker, from - left, right - to);
    }

    private static bool CanToggle(string marker, int left, int right) => left == right && left >= marker.Length &&
        (marker == "*" ? left % 2 == 1 : marker == "**" || left == marker.Length);

    public static MarkupEdit Heading(string document, int start, int length, int level)
    {
        int begin = start;
        while (begin > 0 && document[begin - 1] is not ('\r' or '\n')) begin--;
        int end = start + length;
        while (end < document.Length && document[end] is not ('\r' or '\n')) end++;
        string line = document.Substring(begin, end - begin);
        int marker = 0;
        while (marker < line.Length && marker < 6 && line[marker] == '#') marker++;
        if (marker == 0 || (marker < line.Length && line[marker] is not (' ' or '\t'))) marker = 0;
        int body = marker == 0 ? 0 : marker;
        while (marker > 0 && body < line.Length && line[body] is ' ' or '\t') body++;
        string prefix = level == 0 ? "" : new string('#', level) + " ";
        string result = prefix + line.Substring(body);
        int selection = begin + Math.Max(0, Math.Min(start - begin - body, line.Length - body)) + prefix.Length;
        return new(begin, end - begin, result, selection, Math.Min(length, begin + result.Length - selection));
    }

    public static int OffsetOfLine(string text, int line)
    {
        if (line < 1) return -1;
        int offset = 0;
        for (int current = 1; current < line; current++)
        {
            int end = text.AsSpan(offset).IndexOfAny('\r', '\n');
            if (end < 0) return -1;
            offset += end + 1;
            if (text[offset - 1] == '\r' && offset < text.Length && text[offset] == '\n') offset++;
        }
        return offset;
    }
}
