using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TheReadme;

public static class WindowsIntegration
{
    private const string IntegrationKey = @"Software\ShardApps\TheReadme";
    private const string OwnedPath = "OwnedPath";
    private static string AppDirectory => AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
    private static readonly Lazy<string?> PackageApplicationId = new(ReadPackageApplicationId);
    public static bool IsPackaged => PackageApplicationId.Value is not null;
    public const string ExecutionAliasSettingsUri = "ms-settings:appsfeatures-appexecutionaliases";

    public static bool IsInstalled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(IntegrationKey);
            return SamePath(key?.GetValue("InstallDirectory") as string ?? "", AppDirectory);
        }
    }

    public static string DefaultAppsUri => PackageApplicationId.Value is { } appId
        ? "ms-settings:defaultapps?registeredAUMID=" + Uri.EscapeDataString(appId)
        : "ms-settings:defaultapps?registeredAppUser=" + Uri.EscapeDataString("The Readme");

    public static void Open(string destination) => Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });

    public static string PathStatus()
    {
        using var environment = Registry.CurrentUser.OpenSubKey("Environment");
        using var state = Registry.CurrentUser.OpenSubKey(IntegrationKey);
        if (!ReadPath(environment).Split(';').Any(part => SamePath(part, AppDirectory))) return "Off";
        return SamePath(state?.GetValue(OwnedPath) as string ?? "", AppDirectory) ? "On" : "Already on PATH";
    }

    public static void SetPath(bool enable)
    {
        if (IsPackaged) throw new IOException("Manage the Store version’s command in Windows App execution aliases.");
        using var mutex = new Mutex(false, @"Local\ShardApps.TheReadme.Path");
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new IOException("Windows PATH is busy."); }
        catch (AbandonedMutexException) { }
        try
        {
            using var environment = Registry.CurrentUser.CreateSubKey("Environment");
            using var state = Registry.CurrentUser.CreateSubKey(IntegrationKey);
            string current = ReadPath(environment);
            var parts = current.Split(';').ToList();
            int existing = parts.FindIndex(part => SamePath(part, AppDirectory));
            bool owned = SamePath(state.GetValue(OwnedPath) as string ?? "", AppDirectory);
            if (enable && existing < 0)
            {
                if (AppDirectory.Contains(';')) throw new IOException("This installation folder cannot be added to PATH.");
                string next = current.Length == 0 ? AppDirectory : current + ";" + AppDirectory;
                state.SetValue(OwnedPath, AppDirectory);
                try { WritePath(environment, next); }
                catch { state.DeleteValue(OwnedPath, throwOnMissingValue: false); throw; }
            }
            else if (!enable && owned)
            {
                int addedEntry = parts.FindLastIndex(part => part.Equals(AppDirectory, StringComparison.Ordinal));
                if (addedEntry >= 0)
                {
                    parts.RemoveAt(addedEntry);
                    WritePath(environment, string.Join(";", parts));
                }
                state.DeleteValue(OwnedPath, throwOnMissingValue: false);
            }
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static string ReadPath(RegistryKey? key) => key?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";

    private static void WritePath(RegistryKey key, string value)
    {
        var kind = key.GetValue("Path") is null ? RegistryValueKind.ExpandString : key.GetValueKind("Path");
        key.SetValue("Path", value, kind);
        SendMessageTimeoutW((nint)0xffff, 0x001a, 0, "Environment", 2, 1000, out _);
    }

    internal static bool SamePath(string first, string second) => string.Equals(
        Environment.ExpandEnvironmentVariables(first.Trim().Trim('"')).TrimEnd('\\', '/'),
        second.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string? ReadPackageApplicationId()
    {
        uint capacity = 0;
        const int insufficientBuffer = 122;
        if (GetCurrentApplicationUserModelId(ref capacity, null) != insufficientBuffer || capacity > 1024)
            return null;
        var result = new StringBuilder((int)capacity);
        return GetCurrentApplicationUserModelId(ref capacity, result) == 0 ? result.ToString() : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetCurrentApplicationUserModelId(ref uint length, StringBuilder? appUserModelId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendMessageTimeoutW(nint window, uint message, nint wParam, string lParam, uint flags, uint timeout, out nint result);
}
