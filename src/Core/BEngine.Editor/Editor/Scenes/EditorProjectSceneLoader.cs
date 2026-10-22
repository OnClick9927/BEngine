using BEngine.Serialization;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;

namespace BEngine.Editor;

internal sealed class EditorProjectSceneLoader : ISceneLoader
{
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;
    private readonly string _projectRootPath;

    internal EditorProjectSceneLoader(
        string projectRootPath,
        FileUtility fileSystem,
        PathUtility pathService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _paths = pathService ?? throw new ArgumentNullException(nameof(pathService));
        _projectRootPath = _paths.GetFullPath(projectRootPath);
    }

    internal EditorProjectSceneLoader(string projectRootPath) : this(
        projectRootPath,
        EditorServiceAccess.GetRequiredService<FileUtility>(),
        EditorServiceAccess.GetRequiredService<PathUtility>()) { }

    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        var sourcePath = ResolveScenePath(sceneNameOrPath);
        var scene = SceneAssetSerialization.Load(sourcePath, services);
        scene.path = sourcePath;
        return scene;
    }

    private string ResolveScenePath(string sceneNameOrPath)
    {
        var candidate = _paths.IsPathRooted(sceneNameOrPath)
            ? _paths.GetFullPath(sceneNameOrPath)
            : _paths.GetFullPath(_paths.Combine(_projectRootPath,
                sceneNameOrPath.Replace('/', _paths.DirectorySeparatorChar)));
        if (_fileSystem.FileExists(candidate)) return EnsureInsideProject(candidate);

        var requestedName = _paths.GetFileName(sceneNameOrPath);
        var assetsPath = _paths.Combine(_projectRootPath, "Assets");
        var matches = _fileSystem.EnumerateFiles(assetsPath, "*.scene.yaml", SearchOption.AllDirectories)
            .Where(path => _paths.GetFileName(path).Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
                           SceneName(path).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Scene '{sceneNameOrPath}' was not found in Assets."),
            _ => throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous. Use a project-relative scene path.")
        };
    }

    private string EnsureInsideProject(string path)
    {
        var relative = _paths.GetRelativePath(_projectRootPath, path);
        if (relative == ".." || relative.StartsWith($"..{_paths.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Editor scenes must be located inside the current project.");
        return path;
    }

    private string SceneName(string path)
    {
        var fileName = _paths.GetFileName(path);
        return fileName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scene.yaml".Length]
            : _paths.GetFileNameWithoutExtension(fileName);
    }
}
