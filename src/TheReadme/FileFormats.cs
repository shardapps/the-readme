using System.IO;

namespace TheReadme;

public enum FileKind { Markdown, PlainText, Source }

public sealed record FileFormat(FileKind Kind, string Label)
{
    public bool CanEdit => Kind != FileKind.Source;
}

public static class FileFormats
{
    public static FileFormat Markdown { get; } = new(FileKind.Markdown, "Markdown");
    public static FileFormat PlainText { get; } = new(FileKind.PlainText, "Plain text");
    private static readonly FileFormat Other = new(FileKind.Source, "Text file");

    private static readonly string[] MarkdownExtensions = [".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt"];
    private static readonly HashSet<string> DocumentNames = new(
        ["README", "LICENSE", "LICENCE", "CHANGELOG", "NEWS", "NOTICE", "AUTHORS", "CONTRIBUTING", "COPYING", "SECURITY"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> BinaryExtensions = new(
        [".exe", ".dll", ".sys", ".msi", ".msix", ".appx", ".com", ".lnk", ".url", ".zip", ".7z", ".rar", ".gz",
         ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp",
         ".mp3", ".mp4", ".wav", ".ogg", ".ico", ".woff", ".woff2", ".ttf", ".otf", ".bin", ".class", ".pyc",
         ".so", ".o", ".obj", ".pdb", ".db", ".sqlite", ".iso", ".wasm"], StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, FileFormat> SourceFormats = BuildSourceFormats();

    public static string OpenFilter { get; } = "All files (*.*)|*.*|Markdown and text|" +
        string.Join(';', MarkdownExtensions.Select(extension => "*" + extension).Concat(["*.txt", "*.text", "README", "CHANGELOG", "LICENSE"])) +
        "|Source, data and configuration|" + string.Join(';', SourceFormats.Keys.Select(name => name.StartsWith('.') ? "*" + name : name));

    public static FileFormat ForPath(string path)
    {
        string name = Path.GetFileName(path);
        string extension = Path.GetExtension(name);
        if (MarkdownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) || DocumentNames.Contains(name)) return Markdown;
        if (SourceFormats.TryGetValue(name, out FileFormat? named)) return named;
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) || extension.Equals(".text", StringComparison.OrdinalIgnoreCase))
            return PlainText;
        return SourceFormats.GetValueOrDefault(extension) ?? Other;
    }

    public static bool CanRead(string path) => !BinaryExtensions.Contains(Path.GetExtension(path));

    private static Dictionary<string, FileFormat> BuildSourceFormats()
    {
        var formats = new Dictionary<string, FileFormat>(StringComparer.OrdinalIgnoreCase);
        void Add(string label, string names)
        {
            var format = new FileFormat(FileKind.Source, label);
            foreach (string name in names.Split(' ')) formats.Add(name, format);
        }

        Add("Python", ".py .pyw .pyi");
        Add("C", ".c .h");
        Add("C++", ".cpp .cc .cxx .hpp .hh .hxx .ipp .inl .ixx");
        Add("Java", ".java");
        Add("C#", ".cs .csx");
        Add("JavaScript", ".js .jsx .mjs .cjs");
        Add("TypeScript", ".ts .tsx .mts .cts");
        Add("Rust", ".rs");
        Add("Go", ".go");
        Add("Swift", ".swift");
        Add("Kotlin", ".kt .kts");
        Add("Ruby", ".rb .rake Gemfile Rakefile");
        Add("PHP", ".php .phtml");
        Add("Lua", ".lua");
        Add("R", ".r");
        Add("Dart", ".dart");
        Add("Scala", ".scala .sc");
        Add("F#", ".fs .fsx .fsi");
        Add("Visual Basic", ".vb .vbs");
        Add("Objective-C", ".m .mm");
        Add("Assembly", ".asm .s .inc");
        Add("Haskell", ".hs .lhs");
        Add("Elixir", ".ex .exs");
        Add("Zig", ".zig");
        Add("Shell", ".sh .bash .zsh .fish .bashrc .zshrc");
        Add("PowerShell", ".ps1 .psm1 .psd1");
        Add("Batch", ".bat .cmd");
        Add("HDL", ".hdl");
        Add("Verilog", ".v .vh .sv .svh");
        Add("VHDL", ".vhd .vhdl");
        Add("SQL", ".sql");
        Add("HTML", ".html .htm .xhtml");
        Add("Stylesheet", ".css .scss .sass .less");
        Add("XML", ".xml .xaml .svg .plist .csproj .vbproj .fsproj .props .targets .resx .slnx");
        Add("JSON", ".json .jsonc .jsonl .geojson .ipynb");
        Add("YAML", ".yaml .yml");
        Add("TOML", ".toml");
        Add("Configuration", ".ini .cfg .conf .editorconfig .gitignore .gitattributes .dockerignore .env .lock");
        Add("Delimited text", ".csv .tsv .psv");
        Add("Log", ".log .out");
        Add("Diff", ".diff .patch");
        Add("Text markup", ".rst .adoc .asciidoc .org .tex .typ");
        Add("Build file", "Dockerfile Containerfile Makefile GNUmakefile CMakeLists.txt .mk .cmake .gradle .sln");
        return formats;
    }
}
