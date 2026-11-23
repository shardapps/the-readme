using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TheReadme;

internal static class FootprintCheck
{
    private const ulong Budget = 50_000_000;

    internal static async Task Run(string directory, string? expectedArchitecture, CancellationToken token)
    {
        if (!IsWow64Process2(GetCurrentProcess(), out ushort processMachine, out ushort nativeMachine))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        string architecture = (processMachine == 0 ? nativeMachine : processMachine) switch
        {
            0x014c => "x86",
            0x8664 => "x64",
            0xaa64 => "arm64",
            _ => "unknown"
        };
        if (expectedArchitecture is not null && expectedArchitecture != architecture)
            throw new InvalidOperationException($"Expected {expectedArchitecture}, running {architecture}.");
        int screenWidth = GetSystemMetrics(0);
        int screenHeight = GetSystemMetrics(1);
        Console.WriteLine($"{architecture} process, {screenWidth} × {screenHeight} display, {Environment.ProcessorCount} logical CPUs");
        var preferences = ReaderPreferences.Load(Path.Combine(directory, "preferences.json"));
        var window = new MainWindow(null, preferences) { ShowActivated = false, ShowInTaskbar = false };
        T Control<T>(string name) where T : FrameworkElement => (T)window.FindName(name);
        void Click(string name) => Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ulong highest = 0;
        async Task Sample(string stage)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            ulong peakPrivate = 0, peakTotal = 0, peakCommit = 0;
            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(250, token);
                var counters = Measure();
                peakPrivate = Math.Max(peakPrivate, counters.PrivateWorkingSet.ToUInt64());
                peakTotal = Math.Max(peakTotal, counters.WorkingSet.ToUInt64());
                peakCommit = Math.Max(peakCommit, counters.PrivateCommit.ToUInt64());
            }
            highest = Math.Max(highest, peakPrivate);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1:F2} MB private, {2:F2} MB total working set, {3:F2} MB commit, {4:F2} MB managed",
                stage, peakPrivate / 1e6, peakTotal / 1e6, peakCommit / 1e6, GC.GetTotalMemory(false) / 1e6));
        }

        window.Show();
        await Sample("Home");
        var readme = new StringBuilder("# Workspace guide\n\n![Workspace illustration](cover.jpg)\n\n");
        for (int index = 1; index <= 30; index++)
            readme.Append($"## Section {index}\n\nOpen the **sample** and review *the workspace*. Use the editor to revise the source and save a copy of the changes.\n\n" +
                "> This step can be completed offline. Keep local project files in the same folder.\n\n" +
                "| Tool | Shortcut |\n| --- | --- |\n| Find | Ctrl+F |\n| Replace | Ctrl+H |\n\n" +
                "- [x] Open the local file.\n- [ ] Review the code block.\n\n~~~python\nprint('The Readme')\n~~~\n\n");
        string notes = Path.Combine(directory, "README.md");
        File.WriteAllText(notes, readme.ToString());
        var cover = Application.GetResourceStream(new Uri("pack://application:,,,/Readme;component/Assets/cover.jpg"));
        using (var input = cover.Stream)
        using (var output = File.Create(Path.Combine(directory, "cover.jpg"))) input.CopyTo(output);
        await window.OpenFile(notes);
        await Sample("Read Markdown with a local image");
        Click("SplitButton");
        await Sample("Edit with split preview");
        Click("FindButton");
        Click("ShowReplaceButton");
        Control<TextBox>("FindBox").Text = "sample";
        Control<TextBox>("ReplaceBox").Text = "example";
        Click("ReplaceNextButton");
        await Sample("Find and Replace with live preview");
        if (!Control<TextBox>("Source").Text.Contains("**example**"))
            throw new InvalidOperationException("The footprint check did not exercise a replacement.");
        if (!await window.SaveDocument(notes)) throw new IOException("The benchmark draft could not be saved.");
        Click("CloseFindButton");
        Click("SettingsButton");
        await Sample("Settings over the split workspace");
        window.DismissSettingsAt(new Point(8, 160));
        window.WindowState = WindowState.Maximized;
        await Sample("Maximized split workspace");
        window.WindowState = WindowState.Normal;
        string source = Path.Combine(directory, "source.py");
        File.WriteAllText(source, string.Concat(Enumerable.Repeat("# Local source fixture\nprint('example')\n", 600)));
        await window.OpenFile(source);
        await Sample("Edit source file");
        Click("ToolbarHomeButton");
        await Sample("Return Home");
        window.Close();

        if (highest >= Budget)
            throw new InvalidOperationException($"Private working set reached {highest / 1e6:F2} MB. The release budget is under 50 MB.");
    }

    private static MemoryCounters Measure()
    {
        uint size = (uint)Marshal.SizeOf<MemoryCounters>();
        if (!GetProcessMemoryInfo(GetCurrentProcess(), out var counters, size))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return counters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCounters
    {
        public uint Size;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSet;
        public UIntPtr WorkingSet;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateCommit;
        public UIntPtr PrivateWorkingSet;
        public ulong SharedCommitUsage;
    }

    [DllImport("psapi.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, out MemoryCounters counters, uint size);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(nint process, out ushort processMachine, out ushort nativeMachine);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetrics(int index);
}
