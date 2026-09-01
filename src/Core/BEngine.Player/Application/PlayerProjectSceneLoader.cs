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
        var scene = SceneAssetSerialization.Load(sourcePath, services);
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
        var document = YamlUtility.Deserialize<SceneAssetData>(handle.Value);
        scene = SceneAssetSerialization.Restore(document, services);
        try { RestoreBundledSprites(document, scene); }
        catch
        {
            scene.Dispose();
            throw;
        }
        scene.path = matches[0];
        return true;
    }

    private void RestoreBundledSprites(SceneAssetData document, Scene scene)
    {
        var renderers = scene.QueryComponents<SpriteRenderer>()
            .ToDictionary(renderer => renderer.Id);
        foreach (var component in document.GameObjects.SelectMany(gameObject => gameObject.Components))
        {
            if (!renderers.TryGetValue(component.Id, out var renderer) ||
                !component.Fields.TryGetValue(nameof(SpriteRenderer.sprite), out var reference) ||
                string.IsNullOrWhiteSpace(reference) ||
                !Path.GetExtension(reference).Equals(".png", StringComparison.OrdinalIgnoreCase)) continue;
            renderer.sprite = AssetBundleAssetLoader.LoadSprite(assetBundles!, reference) ??
                              throw new InvalidDataException(
                                  $"AssetBundle scene Sprite '{reference}' is not a bundled TextureImporter Sprite.");
        }
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
