using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace TheReadme;

public enum MarkdownOpeningView { Read, Edit, Split, LastUsed }

[DataContract]
public sealed class ReaderPreferences
{
    private static readonly string SettingsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShardApps", "TheReadme", "preferences.json");
    private string settingsFile = SettingsFile;

    public ReaderPreferences() : this(SettingsFile) { }
    internal ReaderPreferences(string settingsFile) => this.settingsFile = settingsFile;

    [DataMember] public string Theme { get; set; } = "System";
    [DataMember] public int Zoom { get; set; } = 100;
    [DataMember] public bool ShowSidebar { get; set; }
    public MarkdownOpeningView OpeningView { get; set; }
    public MarkdownOpeningView LastMarkdownView { get; set; }
    [DataMember] public bool TypeToEdit { get; set; } = true;
    [DataMember] public bool RememberRecent { get; set; } = true;
    [DataMember] public bool WrapCode { get; set; }
    [DataMember] public bool WrapText { get; set; } = true;
    [DataMember] public bool AutoIndentSource { get; set; } = true;
    [DataMember] public bool AnimateTransitions { get; set; } = true;
    [DataMember] public List<string> Recent { get; set; } = [];

    [DataMember(Name = nameof(OpeningView))]
    private string OpeningViewName
    {
        get => OpeningView.ToString();
        set => OpeningView = ParseView(value);
    }

    [DataMember(Name = nameof(LastMarkdownView))]
    private string LastMarkdownViewName
    {
        get => LastMarkdownView.ToString();
        set => LastMarkdownView = ParseView(value);
    }

    private static MarkdownOpeningView ParseView(string name) =>
        Enum.TryParse(name, true, out MarkdownOpeningView view) && Enum.IsDefined(typeof(MarkdownOpeningView), view)
            ? view : MarkdownOpeningView.Read;

    [OnDeserializing]
    private void RestoreDefaults(StreamingContext context)
    {
        Theme = "System";
        Zoom = 100;
        TypeToEdit = RememberRecent = WrapText = AutoIndentSource = AnimateTransitions = true;
    }

    public static ReaderPreferences Load()
        => Load(SettingsFile);

    internal static ReaderPreferences Load(string path)
    {
        try
        {
            using var input = File.OpenRead(path);
            if (input.Length > 262_144) return new(path);
            var settings = (ReaderPreferences?)new DataContractJsonSerializer(typeof(ReaderPreferences)).ReadObject(input) ?? new(path);
            settings.settingsFile = path;
            settings.Zoom = Math.Max(60, Math.Min(settings.Zoom, 220));
            settings.Recent = settings.RememberRecent ? (settings.Recent ?? []).Where(path => !string.IsNullOrWhiteSpace(path)).Take(8).ToList() : [];
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SerializationException)
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
            using (var output = File.OpenWrite(temporary))
                new DataContractJsonSerializer(typeof(ReaderPreferences)).WriteObject(output, this);
            LocalFiles.MoveReplacing(temporary, settingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SerializationException)
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
