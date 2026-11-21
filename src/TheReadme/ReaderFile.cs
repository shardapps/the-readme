using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TheReadme;

public sealed record ReaderFile(string Path, string Text, FileFormat Format, long Bytes, Encoding? Encoding = null, string? Fingerprint = null)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Encoding[] UnicodeEncodings =
    [
        new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
        new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true)
    ];

    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsPlainText => Format.Kind != FileKind.Markdown;
    public bool IsOnDisk => Fingerprint is not null;

    public static ReaderFile Untitled(string name, string text = "") =>
        new(name, text, FileFormats.ForPath(name), Utf8.GetByteCount(text));

    public static async Task<ReaderFile> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        string fullPath = LocalPath(path);
        if (!FileFormats.CanRead(fullPath)) throw new IOException("Choose a text or source file.");

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024, useAsync: true);
        if (stream.Length > MaxBytes) throw new IOException("This file exceeds the 2 MB reading limit.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxBytes + 1);
        try
        {
            int count = await stream.ReadAtLeastAsync(buffer.AsMemory(0, MaxBytes + 1), MaxBytes + 1, throwOnEndOfStream: false, cancellationToken);
            if (count > MaxBytes) throw new IOException("This file exceeds the 2 MB reading limit.");
            Encoding encoding = UnicodeEncodings.FirstOrDefault(item => buffer.AsSpan(0, count).StartsWith(item.Preamble)) ?? Utf8;
            int skip = encoding.Preamble.Length;
            string content = encoding.GetString(buffer, skip, count - skip);
            EnsureText(content);
            return new ReaderFile(fullPath, content, FileFormats.ForPath(fullPath), count, encoding,
                Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, count))));
        }
        catch (DecoderFallbackException) { throw new IOException("This file needs UTF-8 or Unicode with a byte-order mark."); }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }

    public async Task<ReaderFile> SaveAsync(string destination, string draft, bool overwrite)
    {
        string path = LocalPath(destination);
        if (!FileFormats.CanRead(path)) throw new IOException("Choose a filename for a text or source file.");
        FileFormat format = FileFormats.ForPath(path);
        bool sameFile = IsOnDisk && path.Equals(Path, StringComparison.OrdinalIgnoreCase);
        Encoding encoding = Encoding ?? Utf8;
        string content = PreserveLineEndings(Text, draft);
        int size = checked(encoding.Preamble.Length + encoding.GetByteCount(content));
        if (size > MaxBytes) throw new IOException("This document exceeds the 2 MB save limit.");
        EnsureText(content);
        byte[] bytes = new byte[size];
        encoding.Preamble.CopyTo(bytes);
        encoding.GetBytes(content.AsSpan(), bytes.AsSpan(encoding.Preamble.Length));

        string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".readme-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            {
                await output.WriteAsync(bytes);
                output.Flush(flushToDisk: true);
            }
            if (sameFile)
            {
                ReaderFile current = await OpenAsync(path);
                if (current.Fingerprint != Fingerprint)
                    throw new IOException("The file changed on disk. Use Save As to preserve your edits in another file.");
            }
            File.Move(temporary, path, overwrite: sameFile || overwrite);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new ReaderFile(path, content, format, bytes.Length, encoding, Convert.ToHexString(SHA256.HashData(bytes)));
    }

    private static void EnsureText(string content)
    {
        if (content.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n' or '\f' or '\u001b')))
            throw new IOException("This file contains binary data. Choose a text document.");
    }

    private static string PreserveLineEndings(string original, string draft)
    {
        if (original.Length == 0) return draft.ReplaceLineEndings("\n");
        string? newline = null;
        for (int index = 0; index < original.Length; index++)
        {
            string? next = original[index] switch
            {
                '\r' when index + 1 < original.Length && original[index + 1] == '\n' => "\r\n",
                '\r' => "\r",
                '\n' => "\n",
                _ => null
            };
            if (next is null) continue;
            if (newline is not null && newline != next) return draft;
            newline = next;
            if (next.Length == 2) index++;
        }
        if (newline is null) return draft.ReplaceLineEndings("\n");
        return draft.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Replace("\n", newline, StringComparison.Ordinal);
    }

    private static string LocalPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new IOException("Copy the document to a local drive to use it here.");
        return System.IO.Path.GetFullPath(path);
    }
}
