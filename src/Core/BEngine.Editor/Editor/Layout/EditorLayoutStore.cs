using BEngine.Documents;
using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal sealed class EditorLayoutStore
{
    private readonly string _lastSessionPath;
    private readonly string _layoutsPath;

    public EditorLayoutStore(ProjectSystem.ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _lastSessionPath = workspace.EditorLayoutPath;
        _layoutsPath = Path.Combine(workspace.ProjectSettingsPath, "Layouts");
        Directory.CreateDirectory(_layoutsPath);
    }

    public IReadOnlyList<string> Names => Directory.EnumerateFiles(_layoutsPath, "*.layout.yaml",
            SearchOption.TopDirectoryOnly)
        .Select(path => Path.GetFileName(path)[..^".layout.yaml".Length])
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool HasLastSession => File.Exists(_lastSessionPath);

    public EditorLayoutDocument LoadLastSession() => Document.Load<EditorLayoutDocument>(_lastSessionPath);

    public EditorLayoutDocument Load(string name) =>
        Document.Load<EditorLayoutDocument>(GetNamedPath(name));

    public void SaveLastSession(EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Save(_lastSessionPath);
    }

    public string Save(string name, EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeName(name);
        document.Name = normalized;
        document.ActiveLayout = normalized;
        document.Save(GetNamedPath(normalized));
        return normalized;
    }

    public bool Delete(string name)
    {
        var path = GetNamedPath(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public static string NormalizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var normalized = new string((name ?? string.Empty).Trim()
            .Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character)
            .ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(normalized)) normalized = "Layout";
        return normalized.Length <= 64 ? normalized : normalized[..64].TrimEnd();
    }

    private string GetNamedPath(string name) =>
        Path.Combine(_layoutsPath, $"{NormalizeName(name)}.layout.yaml");
}
