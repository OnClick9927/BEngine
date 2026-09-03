using BEngine.AssetBundles;
using BEngine.Documents;
using BEngine.ProjectSystem;
using BEngine.SceneManagement;
using BEngine.Serialization;

namespace BEngine.Editor;

internal sealed class EditorPlayModeSceneLoader(
    ProjectWorkspace workspace,
    IAssetBundleManager assetBundles) : ISceneLoader
{
    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        var requested = ToProjectRelativePath(sceneNameOrPath);
        var matches = assetBundles.EnumerateAddresses("Assets")
            .Where(address => address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase))
            .Where(address =>
                address.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(address).Equals(Path.GetFileName(requested), StringComparison.OrdinalIgnoreCase) ||
                SceneName(address).Equals(sceneNameOrPath, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
            throw new FileNotFoundException(
                $"Scene '{sceneNameOrPath}' was not found in the Editor virtual AssetBundles.");
        if (matches.Length > 1)
            throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous in the Editor virtual AssetBundles.");

        using var handle = assetBundles.LoadTextAsync(matches[0]).ConfigureAwait(false).GetAwaiter().GetResult();
        var document = YamlUtility.Deserialize<SceneAssetData>(handle.Value);
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
        if (!Path.IsPathRooted(sceneNameOrPath)) return sceneNameOrPath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(sceneNameOrPath);
        var relative = Path.GetRelativePath(workspace.RootPath, fullPath).Replace('\\', '/');
        return relative.StartsWith("../", StringComparison.Ordinal) ? sceneNameOrPath : relative;
    }

    private static string SceneName(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^".scene.yaml".Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }
}
