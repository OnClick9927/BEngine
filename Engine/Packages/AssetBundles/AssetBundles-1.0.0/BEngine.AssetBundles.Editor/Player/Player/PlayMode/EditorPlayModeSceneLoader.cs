using BEngine.AssetBundles;
using BEngine.Serialization;
using BEngine.ProjectSystem;
using BEngine.Platform;
using BEngine.SceneManagement;

namespace BEngine.Editor.Build;

internal sealed class EditorPlayModeSceneLoader(
    ProjectWorkspace workspace,
    IAssetBundleManager assetBundles,
    PathUtility pathService) : ISceneLoader
{
    private readonly IResourceObjectProvider _resources = new SceneResources(assetBundles);

    // Scene migration preserves the direct loader's object identity and exact
    // address rules. The general Resources provider intentionally adds caching.
    private sealed class SceneResources(IAssetBundleManager manager) : IResourceObjectProvider
    {
        public bool TryLoadObject(string path, string folderName, Type type, out BObject value)
        {
            value = AssetBundleAssetLoader.LoadObject(manager, path, type)!;
            return value is not null;
        }
    }

    public Scene LoadScene(string sceneNameOrPath, IServiceProvider services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneNameOrPath);
        var requested = ToProjectRelativePath(sceneNameOrPath);
        var addresses = ((((global::System.Collections.Generic.IEnumerable<string>)(assetBundles.EnumerateAddresses("Assets").Where(address => address.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase)))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
        {
            (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
            var (distinctItems578, distinctComparer578) => distinctItems578.ToHashSet<string>(distinctComparer578).ToArray()});
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
                $"Scene '{sceneNameOrPath}' was not found in the Editor AssetBundles.");
        if (matches.Length > 1)
            throw new InvalidOperationException(
                $"Scene name '{sceneNameOrPath}' is ambiguous in the Editor AssetBundles.");

        if (!assetBundles.TryLoadBytes(matches[0], out var bytes))
            throw new FileNotFoundException(
                $"Scene '{matches[0]}' could not be read from the Editor AssetBundles.", matches[0]);
        var scene = SceneAssetLoading.Restore(SceneAssetLoading.ReadData(bytes), services, _resources);
        RuntimeAssetReferences.BindScenePath(scene, matches[0]);
        return scene;
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
