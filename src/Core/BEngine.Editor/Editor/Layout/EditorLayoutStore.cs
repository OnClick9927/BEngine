using BEngine.Editor.Documents;
using BEngine.Platform;

namespace BEngine.Editor;

internal sealed class EditorLayoutStore
{
    internal const string LastSessionName = "Layout";
    internal static readonly IReadOnlyList<string> BuiltInLayoutNames =
        ["Default", "2 by 3", "Tall", "Wide"];

    private static readonly string[] LegacyLastSessionNames = ["Last Session", "Last Select"];
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;
    private readonly string _lastSessionPath;
    private readonly string _layoutsPath;

    public EditorLayoutStore(ProjectSystem.ProjectWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _fileSystem = EditorServiceAccess.GetRequiredService<FileUtility>();
        _pathService = EditorServiceAccess.GetRequiredService<PathUtility>();
        _layoutsPath = EditorDataPaths.GetProjectLayoutsPath(workspace.RootPath);
        _lastSessionPath = _pathService.Combine(_layoutsPath, "Layout.yaml");
        _fileSystem.CreateDirectory(_layoutsPath);
        MigrateProjectLayouts(workspace);
    }

    public IReadOnlyList<string> Names => _fileSystem.EnumerateFiles(_layoutsPath, "*.layout.yaml",
            SearchOption.TopDirectoryOnly)
        .Select(path => _pathService.GetFileName(path)[..^".layout.yaml".Length])
        .Where(static name => !IsBuiltInName(name))
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<string> AvailableNames => BuiltInLayoutNames.Concat(Names).ToArray();

    public bool HasLastSession => true;

    public EditorLayoutDocument LoadLastSession() => _fileSystem.FileExists(_lastSessionPath)
        ? NormalizeCurrentLayout(YamlUtility.Load<EditorLayoutDocument>(
            _lastSessionPath, _fileSystem, _pathService))
        : CreateCurrentLayout();

    public EditorLayoutDocument Load(string name)
    {
        var normalized = NormalizeName(name);
        if (IsCurrentLayoutName(normalized)) return LoadLastSession();
        if (BuiltInLayoutNames.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            return CreateBuiltInLayout(normalized);
        return YamlUtility.Load<EditorLayoutDocument>(
            GetNamedPath(normalized), _fileSystem, _pathService);
    }

    public void SaveLastSession(EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Name = LastSessionName;
        document.ActiveLayout = NormalizeActiveLayoutName(document.ActiveLayout);
        SaveDocument(document, _lastSessionPath);
    }

    public string Save(string name, EditorLayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var normalized = NormalizeName(name);
        if (IsBuiltInName(normalized))
            throw new InvalidOperationException($"The built-in layout '{normalized}' cannot be overwritten.");
        document.Name = normalized;
        document.ActiveLayout = normalized;
        SaveDocument(document, GetNamedPath(normalized));
        return normalized;
    }

    public string Rename(string name, string newName)
    {
        var normalized = NormalizeName(name);
        var renamed = NormalizeName(newName);
        if (IsBuiltInName(normalized) || IsBuiltInName(renamed))
            throw new InvalidOperationException("Built-in layouts cannot be renamed or replaced.");
        var sourcePath = GetNamedPath(normalized);
        if (!_fileSystem.FileExists(sourcePath))
            throw new FileNotFoundException($"Editor layout '{normalized}' does not exist.", sourcePath);
        var destinationPath = GetNamedPath(renamed);
        if (!sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase) &&
            _fileSystem.FileExists(destinationPath))
            throw new IOException($"Editor layout '{renamed}' already exists.");

        var document = YamlUtility.Load<EditorLayoutDocument>(
            sourcePath, _fileSystem, _pathService);
        document.Name = renamed;
        if (string.Equals(document.ActiveLayout, normalized, StringComparison.OrdinalIgnoreCase))
            document.ActiveLayout = renamed;
        if (sourcePath.Equals(destinationPath, StringComparison.Ordinal))
        {
            SaveDocument(document, sourcePath);
            return renamed;
        }

        if (sourcePath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            var temporaryPath = _pathService.Combine(
                _layoutsPath, $".{Guid.NewGuid():N}.layout.rename");
            _fileSystem.MoveFile(sourcePath, temporaryPath);
            try
            {
                SaveDocument(document, destinationPath);
                _fileSystem.DeleteFile(temporaryPath);
            }
            catch
            {
                try
                {
                    if (_fileSystem.FileExists(destinationPath))
                        _fileSystem.DeleteFile(destinationPath);
                }
                catch { }
                if (_fileSystem.FileExists(temporaryPath))
                    _fileSystem.MoveFile(temporaryPath, sourcePath);
                throw;
            }
            return renamed;
        }

        SaveDocument(document, destinationPath);
        try { _fileSystem.DeleteFile(sourcePath); }
        catch
        {
            try { _fileSystem.DeleteFile(destinationPath); } catch { }
            throw;
        }
        return renamed;
    }

    public bool Delete(string name)
    {
        if (IsBuiltInName(name)) return false;
        var path = GetNamedPath(name);
        if (!_fileSystem.FileExists(path)) return false;
        _fileSystem.DeleteFile(path);
        return true;
    }

    public static string NormalizeName(string name)
    {
        var invalid = EditorServiceAccess.GetRequiredService<PathUtility>()
            .GetInvalidFileNameChars().ToHashSet();
        var normalized = new string((name ?? string.Empty).Trim()
            .Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character)
            .ToArray()).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(normalized)) normalized = LastSessionName;
        return normalized.Length <= 64 ? normalized : normalized[..64].TrimEnd();
    }

    internal static bool IsBuiltInName(string name)
    {
        var normalized = NormalizeName(name);
        return IsCurrentLayoutName(normalized) ||
               BuiltInLayoutNames.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCurrentLayoutName(string name) =>
        name.Equals(LastSessionName, StringComparison.OrdinalIgnoreCase) ||
        LegacyLastSessionNames.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string NormalizeActiveLayoutName(string? name) =>
        string.IsNullOrWhiteSpace(name) || IsCurrentLayoutName(name)
            ? LastSessionName
            : NormalizeName(name);

    private static EditorLayoutDocument CreateCurrentLayout() => new()
    {
        Name = LastSessionName,
        ActiveLayout = LastSessionName,
        WindowWidth = 1500,
        WindowHeight = 920,
        HierarchyWidth = 250,
        InspectorWidth = 340,
        BottomHeight = 220,
        ProjectFoldersWidth = 240,
        ProjectThumbnailSize = 72,
        ShowHierarchy = true,
        ShowInspector = true,
        ShowSceneView = true,
        ShowGameView = true,
        ShowProject = true,
        ShowConsole = true
    };

    private static EditorLayoutDocument CreateBuiltInLayout(string requestedName)
    {
        var name = BuiltInLayoutNames.First(candidate =>
            candidate.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
        var document = CreateCurrentLayout();
        document.Name = name;
        document.ActiveLayout = name;
        document.Windows = StandardWindows();
        document.DockRoot = name switch
        {
            "2 by 3" => Split(true, 0.22f,
                Split(false, 0.5f, Group("Hierarchy"), Group("Project")),
                Split(true, 0.70f,
                    Split(false, 0.5f, Group("Scene"), Group("Game")),
                    Split(false, 0.5f, Group("Inspector"), Group("Console")))),
            "Tall" => Split(true, 0.18f,
                Group("Hierarchy"),
                Split(true, 0.78f,
                    Split(false, 0.72f, Group("Scene"), Group("Game", "Project", "Console")),
                    Group("Inspector"))),
            "Wide" => Split(false, 0.72f,
                Split(true, 0.18f, Group("Hierarchy"),
                    Split(true, 0.78f, Group("Scene", "Game"), Group("Inspector"))),
                Group("Project", "Console")),
            _ => Split(true, 0.18f,
                Group("Hierarchy"),
                Split(true, 0.78f,
                    Split(false, 0.70f, Group("Scene", "Game"), Group("Project", "Console")),
                    Group("Inspector")))
        };
        return document;
    }

    private static List<EditorWindowLayoutDocument> StandardWindows() =>
    [
        Window("Hierarchy", DockArea.Left),
        Window("Scene", DockArea.Center),
        Window("Game", DockArea.Center),
        Window("Inspector", DockArea.Right),
        Window("Project", DockArea.Bottom),
        Window("Console", DockArea.Bottom)
    ];

    private static EditorWindowLayoutDocument Window(string id, DockArea area) => new()
    {
        Id = id,
        State = nameof(EditorWindowState.Normal),
        Docked = true,
        PreferredDockArea = area.ToString()
    };

    private static EditorDockNodeDocument Group(params string[] panels) => new()
    {
        Type = "Group",
        Panels = [.. panels],
        SelectedId = panels.FirstOrDefault()
    };

    private static EditorDockNodeDocument Split(bool sideBySide, float ratio,
        EditorDockNodeDocument first, EditorDockNodeDocument second) => new()
    {
        Type = "Split",
        SideBySide = sideBySide,
        Ratio = ratio,
        First = first,
        Second = second
    };

    private static EditorLayoutDocument NormalizeCurrentLayout(EditorLayoutDocument document)
    {
        document.Name = LastSessionName;
        document.ActiveLayout = NormalizeActiveLayoutName(document.ActiveLayout);
        return document;
    }

    private void MigrateProjectLayouts(ProjectSystem.ProjectWorkspace workspace)
    {
        var legacySession = _pathService.Combine(workspace.ProjectSettingsPath, "EditorLayout.yaml");
        if (_fileSystem.FileExists(legacySession))
        {
            if (!_fileSystem.FileExists(_lastSessionPath))
                MigrateLayoutFile(legacySession, _lastSessionPath, LastSessionName);
            else MigrateNamedLayout(legacySession, $"{workspace.Project.Name} Layout");
        }

        var legacyLayouts = _pathService.Combine(workspace.ProjectSettingsPath, "Layouts");
        if (!_fileSystem.DirectoryExists(legacyLayouts)) return;
        foreach (var source in _fileSystem.EnumerateFiles(legacyLayouts, "*.layout.yaml",
                     SearchOption.TopDirectoryOnly))
        {
            var originalName = _pathService.GetFileName(source)[..^".layout.yaml".Length];
            MigrateNamedLayout(source, originalName, workspace.Project.Name);
        }
        try
        {
            if (!_fileSystem.EnumerateFileSystemEntries(legacyLayouts).Any())
                _fileSystem.DeleteDirectory(legacyLayouts);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void MigrateNamedLayout(string source, string preferredName, string? collisionSuffix = null)
    {
        var normalized = NormalizeName(preferredName);
        if (IsBuiltInName(normalized) || _fileSystem.FileExists(GetNamedPath(normalized)))
        {
            var suffix = NormalizeName(collisionSuffix ?? "Migrated");
            normalized = UniqueLayoutName($"{normalized} ({suffix})");
        }
        MigrateLayoutFile(source, GetNamedPath(normalized), normalized);
    }

    private string UniqueLayoutName(string preferredName)
    {
        var baseName = NormalizeName(preferredName);
        var candidate = baseName;
        for (var number = 2;
             IsBuiltInName(candidate) || _fileSystem.FileExists(GetNamedPath(candidate));
             number++)
            candidate = NormalizeName($"{baseName} {number}");
        return candidate;
    }

    private void MigrateLayoutFile(string source, string destination, string identity)
    {
        _fileSystem.CreateDirectory(_pathService.GetDirectoryName(destination)!);
        try
        {
            var document = YamlUtility.Load<EditorLayoutDocument>(
                source, _fileSystem, _pathService);
            document.Name = identity;
            document.ActiveLayout = identity;
            SaveDocument(document, destination);
            _fileSystem.DeleteFile(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A running editor can briefly own the old file. Retry on the next launch.
        }
    }

    private string GetNamedPath(string name) =>
        _pathService.Combine(_layoutsPath, $"{NormalizeName(name)}.layout.yaml");

    private void SaveDocument(EditorLayoutDocument document, string path) =>
        _fileSystem.WriteAllText(_pathService.GetFullPath(path), YamlUtility.Serialize(document));
}
