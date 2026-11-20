using System.IO;
using System.Buffers;
using System.Text;

namespace TheReadme;

public sealed record ReaderFile(string Path, string Text, bool IsPlainText, long Bytes)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt"];

    public string Name => System.IO.Path.GetFileName(Path);

    public static bool Supports(string path)
    {
        string extension = System.IO.Path.GetExtension(path);
        return extension.Length == 0 || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || MarkdownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<ReaderFile> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new IOException("Copy the document to a local drive before opening it.");
        string fullPath = System.IO.Path.GetFullPath(path);
        if (!Supports(fullPath))
            throw new IOException("Choose a Markdown, text, or extensionless README file.");

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024, useAsync: true);
        if (stream.Length > MaxBytes) throw new IOException("This file exceeds the 2 MB reading limit.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxBytes + 1);
        try
        {
            int count = await stream.ReadAtLeastAsync(buffer.AsMemory(0, MaxBytes + 1), MaxBytes + 1, throwOnEndOfStream: false, cancellationToken);
            if (count > MaxBytes) throw new IOException("This file exceeds the 2 MB reading limit.");

            using var reader = new StreamReader(new MemoryStream(buffer, 0, count), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
            string content = reader.ReadToEnd();
            if (content.Contains('\0')) throw new IOException("This file contains binary data. Choose a text document.");
            return new ReaderFile(fullPath, content, System.IO.Path.GetExtension(fullPath).Equals(".txt", StringComparison.OrdinalIgnoreCase), count);
        }
        catch (DecoderFallbackException) { throw new IOException("Save this file as UTF-8 or Unicode with a byte-order mark, then reopen it."); }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}
