using System.IO;

namespace TheReadme;

public static class DocumentLinks
{
    public static bool TryExternal(string text, out Uri? uri) =>
        Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme is "https" or "http" or "mailto";

    public static string? LocalPath(string documentPath, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative)) return null;
        string decoded = Uri.UnescapeDataString(relative.Split('#', '?')[0]);
        if (decoded.Length == 0 || Path.IsPathRooted(decoded) || decoded.Contains(':') || decoded.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return null;
        try
        {
            string root = Path.GetDirectoryName(documentPath)!;
            string full = Path.GetFullPath(decoded, root);
            string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
            if (root.EndsWith(Path.DirectorySeparatorChar)) prefix = root;
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (ArgumentException) { return null; }
    }
}
