using BEngine.Editor.Documents;

namespace BEngine.Editor;

internal sealed class EditorLayoutStore
{
    internal const string LastSessionName = "Last Session";
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
        .Where(static name => !IsBuiltInName(name))
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public bool HasLastSession => File.Exists(_lastSessionPath);

    public EditorLayoutDocument LoadLastSession() => YamlUtility.Load<EditorLayoutDocument>(_lastSessionPath);

    public EditorLayoutDocument Load(string name) =>
        YamlUtility.Load<EditorLayoutDocument>(GetNamedPath(name));

    public void SaveLastSession(EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        YamlUtility.Save(document, _lastSessionPath);
    }

    public string Save(string name, EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeName(name);
        if (IsBuiltInName(normalized))
            throw new InvalidOperationException($"The built-in layout '{normalized}' cannot be overwritten.");
        document.Name = normalized;
        document.ActiveLayout = normalized;
        YamlUtility.Save(document, GetNamedPath(normalized));
        return normalized;
    }

    public string Rename(string name, string newName)
    {
        var normalized = NormalizeName(name);
        var renamed = NormalizeName(newName);
        if (IsBuiltInName(normalized) || IsBuiltInName(renamed))
            throw new InvalidOperationException("Built-in layouts cannot be renamed or replaced.");
        var sourcePath = GetNamedPath(normalized);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Editor layout '{normalized}' does not exist.", sourcePath);
        var destinationPath = GetNamedPath(renamed);
        if (!sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(destinationPath))
            throw new IOException($"Editor layout '{renamed}' already exists.");

        var document = YamlUtility.Load<EditorLayoutDocument>(sourcePath);
        document.Name = renamed;
        if (string.Equals(document.ActiveLayout, normalized, StringComparison.OrdinalIgnoreCase))
            document.ActiveLayout = renamed;
        if (sourcePath.Equals(destinationPath, StringComparison.Ordinal))
        {
            YamlUtility.Save(document, sourcePath);
            return renamed;
        }

        if (sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            var temporaryPath = Path.Combine(_layoutsPath, $".{Guid.NewGuid():N}.layout.rename");
            File.Move(sourcePath, temporaryPath);
            try
            {
                YamlUtility.Save(document, destinationPath);
                File.Delete(temporaryPath);
            }
            catch
            {
                try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { }
                if (File.Exists(temporaryPath)) File.Move(temporaryPath, sourcePath);
                throw;
            }
            return renamed;
        }

        YamlUtility.Save(document, destinationPath);
        try { File.Delete(sourcePath); }
        catch
        {
            try { File.Delete(destinationPath); } catch { }
            throw;
        }
        return renamed;
    }

    public bool Delete(string name)
    {
        if (IsBuiltInName(name)) return false;
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

    internal static bool IsBuiltInName(string name) =>
        NormalizeName(name).Equals(LastSessionName, StringComparison.OrdinalIgnoreCase);

    private string GetNamedPath(string name) =>
        Path.Combine(_layoutsPath, $"{NormalizeName(name)}.layout.yaml");
}
