using BEngine.AssetBundles;
using BEngine.Serialization;
using BEngine.ProjectSystem;
using BEngine.Platform;
using BEngine.SceneManagement;
using System.Text;

namespace BEngine.Editor;

internal sealed class EditorPlayModeSceneLoader(
    ProjectWorkspace workspace,
    IAssetBundleManager assetBundles,
    PathUtility pathService) : ISceneLoader
{
    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        var requested = ToProjectRelativePath(sceneNameOrPath);
        var addresses = assetBundles.EnumerateAddresses("Assets")
            .Where(address => address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = addresses.Where(address => address.Equals(requested, StringComparison.OrdinalIgnoreCase))
            .Take(1).ToArray();
        if (matches.Length == 0 && !requested.Contains('/'))
            matches = addresses.Where(address =>
                pathService.GetFileName(address).Equals(
                    pathService.GetFileName(requested), StringComparison.OrdinalIgnoreCase) ||
                SceneName(address).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
            throw new FileNotFoundException(
                $"Scene '{sceneNameOrPath}' was not found in the Editor virtual AssetBundles.");
        if (matches.Length > 1)
            throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous in the Editor virtual AssetBundles.");

        if (!assetBundles.TryLoadBytes(matches[0], out var bytes))
            throw new FileNotFoundException(
                $"Scene '{matches[0]}' disappeared from the Editor virtual AssetBundles.", matches[0]);
        var document = YamlUtility.Deserialize<SceneAssetData>(Encoding.UTF8.GetString(bytes));
        var scene = SceneAssetSerialization.Restore(document, services);
        try { RestoreBundledSprites(document, scene); }
        catch
        {
            scene.Dispose();
            throw;
        }
        scene.path = matches[0];
        return scene;
    }

    private void RestoreBundledSprites(SceneAssetData document, Scene scene)
    {
        var renderers = scene.QueryComponents<SpriteRenderer>().ToDictionary(renderer => renderer.Id);
        foreach (var component in document.GameObjects.SelectMany(gameObject => gameObject.Components))
        {
            if (!renderers.TryGetValue(component.Id, out var renderer) ||
                !component.Fields.TryGetValue(nameof(SpriteRenderer.sprite), out var reference) ||
                string.IsNullOrWhiteSpace(reference)) continue;
            var bundled = AssetBundleAssetLoader.LoadSprite(assetBundles, reference);
            if (bundled is not null) renderer.sprite = bundled;
            else if (renderer.sprite is null)
                throw new InvalidDataException(
                    $"AssetBundle scene Sprite '{reference}' is not a bundled TextureImporter Sprite.");
        }
    }

    private string ToProjectRelativePath(string sceneNameOrPath)
    {
        if (!pathService.IsPathRooted(sceneNameOrPath)) return sceneNameOrPath.Replace('\\', '/').TrimStart('/');
        var fullPath = pathService.GetFullPath(sceneNameOrPath);
        var relative = pathService.GetRelativePath(workspace.RootPath, fullPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? sceneNameOrPath : relative;
    }

    private string SceneName(string path)
    {
        var fileName = pathService.GetFileName(path);
        return fileName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scene.yaml".Length]
            : pathService.GetFileNameWithoutExtension(fileName);
    }
}
