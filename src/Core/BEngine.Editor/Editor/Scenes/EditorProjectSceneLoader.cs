using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;

namespace BEngine.Editor;

internal sealed class EditorProjectSceneLoader(string projectRootPath) : ISceneLoader
{
    private readonly string _projectRootPath = Path.GetFullPath(projectRootPath);

    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        var sourcePath = ResolveScenePath(sceneNameOrPath);
        var scene = SceneAssetSerialization.Load(sourcePath, services);
        scene.path = sourcePath;
        return scene;
    }

    private string ResolveScenePath(string sceneNameOrPath)
    {
        var candidate = Path.IsPathRooted(sceneNameOrPath)
            ? Path.GetFullPath(sceneNameOrPath)
            : Path.GetFullPath(Path.Combine(_projectRootPath,
                sceneNameOrPath.Replace('/', Path.DirectorySeparatorChar)));
        if (File.Exists(candidate)) return EnsureInsideProject(candidate);

        var requestedName = Path.GetFileName(sceneNameOrPath);
        var assetsPath = Path.Combine(_projectRootPath, "Assets");
        var matches = Directory.EnumerateFiles(assetsPath, "*.scene.yaml", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
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
        var relative = Path.GetRelativePath(_projectRootPath, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Editor scenes must be located inside the current project.");
        return path;
    }

    private static string SceneName(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scene.yaml".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}
