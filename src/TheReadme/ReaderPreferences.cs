using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TheReadme;

[JsonConverter(typeof(JsonStringEnumConverter<MarkdownOpeningView>))]
public enum MarkdownOpeningView { Read, Edit, Split, LastUsed }

public sealed class ReaderPreferences
{
    private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShardApps", "TheReadme", "preferences.json");
    private string settingsFile;

    public ReaderPreferences() : this(SettingsFile) { }
    internal ReaderPreferences(string settingsFile) => this.settingsFile = settingsFile;

    public string Theme { get; set; } = "System";
    public int Zoom { get; set; } = 100;
    public bool ShowSidebar { get; set; }
    public MarkdownOpeningView OpeningView { get; set; }
    public MarkdownOpeningView LastMarkdownView { get; set; }
    public bool TypeToEdit { get; set; } = true;
    public bool RememberRecent { get; set; } = true;
    public bool WrapCode { get; set; }
    public bool WrapText { get; set; } = true;
    public bool AutoIndentSource { get; set; } = true;
    public bool AnimateTransitions { get; set; } = true;
    public List<string> Recent { get; set; } = [];

    public static ReaderPreferences Load()
        => Load(SettingsFile);

    internal static ReaderPreferences Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<ReaderPreferences>(File.ReadAllText(path)) ?? new(path);
            settings.settingsFile = path;
            settings.Zoom = Math.Clamp(settings.Zoom, 60, 220);
            settings.Recent = settings.RememberRecent ? (settings.Recent ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).Take(8).ToList() : [];
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(path);
        }
    }

    public void Save()
    {
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
            temporary = settingsFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, settingsFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public void Record(string path)
    {
        if (!RememberRecent) return;
        Recent = Recent.Where(item => !item.Equals(path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(8).ToList();
        Save();
    }
}
