using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;
using System.Text;

namespace BEngine.Player;

internal sealed class PlayerProjectSceneLoader(
    ProjectWorkspace workspace,
    IAssetBundleManager? assetBundles = null,
    PlayerBuiltInResourceProvider? builtInResources = null) : ISceneLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        if (TryLoadBuiltInScene(sceneNameOrPath, services, out var builtInScene)) return builtInScene;
        if (TryLoadBundledScene(sceneNameOrPath, services, out var bundledScene)) return bundledScene;
        var sourcePath = ResolveScenePath(sceneNameOrPath);
        var scene = SceneAssetSerialization.Load(sourcePath, services);
        scene.path = sourcePath;
        return scene;
    }

    private bool TryLoadBuiltInScene(
        string sceneNameOrPath,
        IServiceProvider services,
        out Scene scene)
    {
        scene = null!;
        if (builtInResources is null) return false;
        var address = ResolveSceneAddress(
            sceneNameOrPath, builtInResources.EnumerateAddresses("Assets"), "built-in AOT archive");
        if (address is null) return false;
        var yaml = StrictUtf8.GetString(builtInResources.ReadBytes(address));
        scene = SceneAssetSerialization.Deserialize(yaml, services);
        scene.path = address;
        return true;
    }

    private bool TryLoadBundledScene(
        string sceneNameOrPath,
        IServiceProvider services,
        out Scene scene)
    {
        scene = null!;
        if (assetBundles is not { IsInitialized: true, ActiveCatalog: not null }) return false;
        var address = ResolveSceneAddress(
            sceneNameOrPath, assetBundles.EnumerateAddresses("Assets"), "active AssetBundle catalog");
        if (address is null) return false;
        using var handle = assetBundles.LoadTextAsync(address).ConfigureAwait(false).GetAwaiter().GetResult();
        var document = YamlUtility.Deserialize<SceneAssetData>(handle.Value);
        scene = SceneAssetSerialization.Restore(document, services);
        try { RestoreBundledSprites(document, scene); }
        catch
        {
            scene.Dispose();
            throw;
        }
        scene.path = address;
        return true;
    }

    private string? ResolveSceneAddress(
        string sceneNameOrPath,
        IEnumerable<string> addresses,
        string sourceDescription)
    {
        var requested = ToProjectRelativePath(sceneNameOrPath);
        var candidates = addresses
            .Where(address => address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exact = candidates.FirstOrDefault(address =>
            address.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        var matches = candidates
            .Where(address =>
                Path.GetFileName(address).Equals(Path.GetFileName(requested), StringComparison.OrdinalIgnoreCase) ||
                SceneName(address).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous in the {sourceDescription}.")
        };
    }

    private void RestoreBundledSprites(SceneAssetData document, Scene scene)
    {
        var renderers = scene.QueryComponents<SpriteRenderer>()
            .ToDictionary(renderer => renderer.Id);
        foreach (var component in document.GameObjects.SelectMany(gameObject => gameObject.Components))
        {
            if (!renderers.TryGetValue(component.Id, out var renderer) ||
                !component.Fields.TryGetValue(nameof(SpriteRenderer.sprite), out var reference) ||
                string.IsNullOrWhiteSpace(reference)) continue;
            var bundled = AssetBundleAssetLoader.LoadSprite(assetBundles!, reference);
            if (bundled is not null) renderer.sprite = bundled;
            else if (renderer.sprite is null)
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
