using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.AssetBundles;
using BEngine.Platform;
using System.Text;

namespace BEngine.Player;

internal sealed class PlayerProjectSceneLoader : ISceneLoader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ProjectWorkspace _workspace;
    private readonly IAssetBundleManager? _assetBundles;
    private readonly PlayerBuiltInResourceProvider? _builtInResources;
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _paths;

    internal PlayerProjectSceneLoader(
        ProjectWorkspace workspace,
        IAssetBundleManager? assetBundles = null,
        PlayerBuiltInResourceProvider? builtInResources = null,
        FileUtility? fileSystem = null,
        PathUtility? pathService = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _assetBundles = assetBundles;
        _builtInResources = builtInResources;
        _fileSystem = fileSystem ?? RuntimeServiceAccess.FileSystem;
        _paths = pathService ?? RuntimeServiceAccess.PathService;
    }

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
        if (_builtInResources is null) return false;
        var address = ResolveSceneAddress(
            sceneNameOrPath, _builtInResources.EnumerateAddresses("Assets"), "built-in AOT archive");
        if (address is null) return false;
        var yaml = StrictUtf8.GetString(_builtInResources.ReadBytes(address));
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
        if (_assetBundles is not { IsInitialized: true, ActiveCatalog: not null }) return false;
        var address = ResolveSceneAddress(
            sceneNameOrPath, _assetBundles.EnumerateAddresses("Assets"), "active AssetBundle catalog");
        if (address is null) return false;
        if (!_assetBundles.TryLoadBytes(address, out var bytes))
            throw new InvalidOperationException(
                $"Scene '{address}' is not resident in the active AssetBundle release.");
        var document = YamlUtility.Deserialize<SceneAssetData>(StrictUtf8.GetString(bytes));
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
                _paths.GetFileName(address).Equals(
                    _paths.GetFileName(requested), StringComparison.OrdinalIgnoreCase) ||
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
            var bundled = AssetBundleAssetLoader.LoadSprite(_assetBundles!, reference);
            if (bundled is not null) renderer.sprite = bundled;
            else if (renderer.sprite is null)
                throw new InvalidDataException(
                    $"AssetBundle scene Sprite '{reference}' is not a bundled TextureImporter Sprite.");
        }
    }

    private string ToProjectRelativePath(string sceneNameOrPath)
    {
        if (!_paths.IsPathRooted(sceneNameOrPath))
            return sceneNameOrPath.Replace('\\', '/').TrimStart('/');
        var fullPath = _paths.GetFullPath(sceneNameOrPath);
        var relative = _paths.GetRelativePath(_workspace.RootPath, fullPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? sceneNameOrPath : relative;
    }

    private string ResolveScenePath(string sceneNameOrPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        var candidate = _paths.IsPathRooted(sceneNameOrPath)
            ? _paths.GetFullPath(sceneNameOrPath)
            : _workspace.ResolveInside(sceneNameOrPath.Replace('\\', '/'));
        if (_fileSystem.FileExists(candidate)) return EnsureInsideProject(candidate);

        var requestedFile = _paths.GetFileName(sceneNameOrPath);
        var matches = _fileSystem.EnumerateFiles(
                _workspace.AssetsPath, "*.scene.yaml", SearchOption.AllDirectories)
            .Where(path => _paths.GetFileName(path).Equals(requestedFile, StringComparison.OrdinalIgnoreCase) ||
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
        var relative = _paths.GetRelativePath(_workspace.RootPath, path);
        if (relative == ".." || relative.StartsWith($"..{_paths.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            throw new InvalidOperationException("Player scenes must be located inside the project.");
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
