using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;

namespace BEngine.Player;

internal sealed class PlayerProjectSceneLoader(
    ProjectWorkspace workspace,
    IAssetBundleManager? assetBundles = null) : ISceneLoader
{
    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        if (TryLoadBundledScene(sceneNameOrPath, services, out var bundledScene)) return bundledScene;
        var sourcePath = ResolveScenePath(sceneNameOrPath);
        var scene = Document.LoadBObject<SceneDocument, Scene>(sourcePath, services);
        scene.path = sourcePath;
        return scene;
    }

    private bool TryLoadBundledScene(
        string sceneNameOrPath,
        IServiceProvider services,
        out Scene scene)
    {
        scene = null!;
        if (assetBundles is not { IsInitialized: true, ActiveCatalog: not null }) return false;
        var requested = ToProjectRelativePath(sceneNameOrPath);
        var addresses = assetBundles.EnumerateAddresses("Assets")
            .Where(address => address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase));
        var matches = addresses.Where(address =>
                address.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(address).Equals(Path.GetFileName(requested), StringComparison.OrdinalIgnoreCase) ||
                SceneName(address).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (matches.Length == 0) return false;
        if (matches.Length > 1)
            throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous in the active AssetBundle catalog.");
        using var handle = assetBundles.LoadTextAsync(matches[0]).ConfigureAwait(false).GetAwaiter().GetResult();
        scene = Document.FromYaml<SceneDocument>(handle.Value)
            .ToBObject(new DocumentConversionContext(matches[0], services)) as Scene ??
                throw new InvalidDataException($"AssetBundle scene '{matches[0]}' could not be deserialized.");
        scene.path = matches[0];
        return true;
    }

    private string ToProjectRelativePath(string sceneNameOrPath)
    {
        if (!Path.IsPathRooted(sceneNameOrPath)) return sceneNameOrPath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(sceneNameOrPath);
        var relative = Path.GetRelativePath(workspace.RootPath, fullPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? sceneNameOrPath : relative;
    }

    private string ResolveScenePath(string sceneNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        var candidate = Path.IsPathRooted(sceneNameOrPath)
            ? Path.GetFullPath(sceneNameOrPath)
            : workspace.ResolveInside(sceneNameOrPath.Replace('\\', '/'));
        if (File.Exists(candidate)) return EnsureInsideProject(candidate);

        var requestedFile = Path.GetFileName(sceneNameOrPath);
        var matches = Directory.EnumerateFiles(workspace.AssetsPath, "*.scene.yaml", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Equals(requestedFile, StringComparison.OrdinalIgnoreCase) ||
                           SceneName(path).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Scene '{sceneNameOrPath}' was not found."),
            _ => throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous. Use a project-relative path.")
        };
    }

    private string EnsureInsideProject(string path)
    {
        var relative = Path.GetRelativePath(workspace.RootPath, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Player scenes must be located inside the project.");
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
