using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace TheReadme;

internal static class LocalFiles
{
    public static void MoveReplacing(string source, string destination)
    {
        const uint replaceExisting = 1;
        if (MoveFileExW(source, destination, replaceExisting)) return;
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        throw new IOException(error.Message, error);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileExW(string source, string destination, uint flags);
}
