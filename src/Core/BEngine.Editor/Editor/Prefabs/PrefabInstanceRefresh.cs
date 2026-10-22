using BEngine.Serialization;

namespace BEngine.Editor;

public static partial class PrefabUtility
{
    /// <summary>Merge source changes into an existing instance, preserving its overrides and live identity.</summary>
    public static bool RefreshPrefabInstance(GameObject instanceRoot)
    {
        EnsurePrefabEditing(instanceRoot);
        var root = GetNearestPrefabInstanceRoot(instanceRoot) ?? instanceRoot;
        if (root.PrefabInstance is null) return false;
        var before = LiveSnapshot(root);
        var references = PrefabAssetOperations.Traverse(root).Where(item => item.PrefabInstance is not null)
            .Select(item => PrefabMerge.Clone(item.PrefabInstance!)).ToList();
        var desired = new SceneAssetData
        {
            Id = Guid.NewGuid(), Name = root.name, GameObjects = PrefabMerge.Clone(before),
            PrefabInstances = PrefabMerge.Clone(references)
        };
        PrefabResolver.ResolveReferences(desired.GameObjects, desired.PrefabInstances);
        if (root.PrefabAssetId is null)
        {
            // Prefab Mode contents carry provenance but remain disconnected authoring objects.
            var asset = PrefabResolver.Load(root.PrefabInstance.Asset);
            foreach (var item in desired.GameObjects.Concat(desired.PrefabInstances.SelectMany(item => item.Baseline)))
            {
                if (item.PrefabAsset == asset.assetId) { item.PrefabAsset = null; item.PrefabSource = null; }
                if (item.Transform.PrefabAsset == asset.assetId) { item.Transform.PrefabAsset = null; item.Transform.PrefabSource = null; }
                foreach (var component in item.Components)
                    if (component.PrefabAsset == asset.assetId) { component.PrefabAsset = null; component.PrefabSource = null; }
            }
        }
        var sameObjects = PrefabMerge.Equal(before.OrderBy(item => item.Id).ToArray(), desired.GameObjects.OrderBy(item => item.Id).ToArray());
        if (sameObjects && PrefabMerge.Equal(references, desired.PrefabInstances)) return false;
        if (sameObjects && root.PrefabAssetId is null)
        {
            // Saving Prefab Mode already persisted these values. Advance provenance without
            // immediately turning the just-saved stage dirty again.
            var byRoot = desired.PrefabInstances.ToDictionary(item => item.Root);
            foreach (var item in PrefabAssetOperations.Traverse(root))
                item.PrefabInstance = byRoot.TryGetValue(item.Id, out var reference) ? PrefabMerge.Clone(reference) : null;
            RaiseInstanceUpdated(root);
            return true;
        }
        using var context = new OverrideContext(root, desired);
        EditInstance(root, "Refresh Prefab Instance", InteractionMode.AutomatedAction, () => RestoreHierarchy(context));
        return true;
    }
}

internal static class PrefabInstanceRefresh
{
    private static bool _pending;
    private static bool _refreshing;
    internal static void Request() => _pending = true;

    internal static void Update()
    {
        if (!_pending || _refreshing || !EditorAssetWritePolicy.CanWrite || EditorApplication.isCompiling ||
            EditorApplication.isUpdating || EditorBridge.Host is not { } host) return;
        _pending = false; _refreshing = true;
        try
        {
            EditorPreviewRegistry.StopAll();
            var scenes = host.OpenScenes?.ToList() ?? [];
            if (host.CurrentPrefabStage is { } stage && !scenes.Contains(stage.scene)) scenes.Add(stage.scene);
            var roots = scenes.Where(scene => !scene.IsRuntimeOnly).SelectMany(scene => scene.gameObjects)
                .Where(item => !item.IsRuntimeOnly && item.PrefabInstance is not null &&
                    !HasPrefabAncestor(item)).ToArray();
            foreach (var root in roots)
            {
                if (root.isDestroyed) continue;
                try { PrefabUtility.RefreshPrefabInstance(root); }
                catch (Exception error) { Debug.LogError($"Could not refresh prefab '{root.name}': {error.Message}"); }
            }
            foreach (var window in EditorWindow.EnumerateOpenWindows().OfType<PrefabOverridesWindow>()) window.Controller.Refresh();
        }
        finally { _refreshing = false; }
    }

    private static bool HasPrefabAncestor(GameObject item)
    {
        for (var parent = item.transform.parent; parent is not null; parent = parent.parent)
            if (parent.gameObject.PrefabInstance is not null) return true;
        return false;
    }
}
