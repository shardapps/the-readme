using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace TheReadme;

internal static class RenderPolicy
{
    private static readonly bool LimitedHardware = Environment.ProcessorCount <= 4 || HasLimitedMemory();
    internal static int ImagePixels => LimitedHardware ? 150_000 : 300_000;

    internal static void Initialize() => RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

    internal static bool LimitMotion(int pixelWidth, int pixelHeight) =>
        LimitedHardware || (long)pixelWidth * pixelHeight > 2_073_600;

    private static bool HasLimitedMemory()
    {
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical <= 8UL * 1024 * 1024 * 1024;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        internal uint Length;
        internal uint Load;
        internal ulong TotalPhysical;
        internal ulong AvailablePhysical;
        internal ulong TotalPageFile;
        internal ulong AvailablePageFile;
        internal ulong TotalVirtual;
        internal ulong AvailableVirtual;
        internal ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
