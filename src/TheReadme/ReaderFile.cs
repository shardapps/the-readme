using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TheReadme;

public sealed record ReaderFile(string Path, string Text, FileFormat Format, long Bytes, Encoding? Encoding = null, string? Fingerprint = null)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly (Encoding Encoding, byte[] Preamble)[] UnicodeEncodings =
    [
        WithPreamble(new UTF32Encoding(false, true, true)), WithPreamble(new UTF32Encoding(true, true, true)),
        WithPreamble(new UTF8Encoding(true, true)), WithPreamble(new UnicodeEncoding(false, true, true)),
        WithPreamble(new UnicodeEncoding(true, true, true))
    ];

    private static (Encoding, byte[]) WithPreamble(Encoding encoding) => (encoding, encoding.GetPreamble());

    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsPlainText => Format.Kind != FileKind.Markdown;
    public bool IsOnDisk => Fingerprint is not null;

    public static ReaderFile Untitled(string name, string text = "") =>
        new(name, text, FileFormats.ForPath(name), Utf8.GetByteCount(text));

    public static async Task<ReaderFile> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        string fullPath = LocalPath(path);
        if (!FileFormats.CanRead(fullPath)) throw new IOException("Choose a text or source file.");

        using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, useAsync: true);
        long size = stream.Length;
        if (size > MaxBytes) throw new IOException("This file exceeds the 2 MB reading limit.");
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)size + 1);
        try
        {
            int count = 0;
            while (true)
            {
                int read = await stream.ReadAsync(buffer, count, Math.Min(buffer.Length, MaxBytes + 1) - count, cancellationToken);
                if (read == 0) break;
                count += read;
                if (count > MaxBytes) throw new IOException("This file exceeds the 2 MB reading limit.");
                if (count != buffer.Length) continue;
                byte[] larger = ArrayPool<byte>.Shared.Rent(Math.Min(buffer.Length * 2, MaxBytes + 1));
                Buffer.BlockCopy(buffer, 0, larger, 0, count);
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                buffer = larger;
            }
            var detected = UnicodeEncodings.FirstOrDefault(item => buffer.AsSpan(0, count).StartsWith(item.Preamble));
            Encoding encoding = detected.Encoding ?? Utf8;
            int skip = detected.Preamble?.Length ?? 0;
            string content = encoding.GetString(buffer, skip, count - skip);
            EnsureText(content);
            return new ReaderFile(fullPath, content, FileFormats.ForPath(fullPath), count, encoding, FingerprintOf(buffer, count));
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
        byte[] preamble = encoding.GetPreamble();
        int size = checked(preamble.Length + encoding.GetByteCount(content));
        if (size > MaxBytes) throw new IOException("This document exceeds the 2 MB save limit.");
        EnsureText(content);
        byte[] bytes = new byte[size];
        preamble.CopyTo(bytes, 0);
        encoding.GetBytes(content, 0, content.Length, bytes, preamble.Length);

        string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, $".readme-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
            {
                await output.WriteAsync(bytes, 0, bytes.Length);
                output.Flush(flushToDisk: true);
            }
            if (sameFile)
            {
                ReaderFile current = await OpenAsync(path);
                if (current.Fingerprint != Fingerprint)
                    throw new IOException("The file changed on disk. Use Save As to preserve your edits in another file.");
            }
            if (sameFile || overwrite) LocalFiles.MoveReplacing(temporary, path);
            else File.Move(temporary, path);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return new ReaderFile(path, content, format, bytes.Length, encoding, FingerprintOf(bytes, bytes.Length));
    }

    private static string FingerprintOf(byte[] bytes, int count)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(bytes, 0, count));
    }

    private static void EnsureText(string content)
    {
        if (content.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n' or '\f' or '\u001b')))
            throw new IOException("This file contains binary data. Choose a text document.");
    }

    private static string PreserveLineEndings(string original, string draft)
    {
        if (original.Length == 0) return NormalizeLineEndings(draft);
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
        string normalized = NormalizeLineEndings(draft);
        return newline is null or "\n" ? normalized : normalized.Replace("\n", newline);
    }

    private static string NormalizeLineEndings(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string LocalPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            throw new IOException("Copy the document to a local drive to use it here.");
        return System.IO.Path.GetFullPath(path);
    }
}
