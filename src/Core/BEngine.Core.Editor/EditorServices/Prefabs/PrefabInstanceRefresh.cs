using BEngine.Serialization;

namespace BEngine.Editor;

public static partial class PrefabUtility
{
    /// <summary>Merge source changes into an existing instance, preserving its overrides and live identity.</summary>
    public static bool RefreshPrefabInstance(GameObject instanceRoot)
    {
        EnsurePrefabEditing(instanceRoot);
        var root = GetNearestPrefabInstanceRoot(instanceRoot) ?? instanceRoot;
        var refresh = EditorPrefabRefresh.Prepare(root);
        if (refresh is null || !refresh.HasChanges) return false;
        if (refresh.MetadataOnly)
        {
            refresh.ApplyMetadata(); RaiseInstanceUpdated(root); return true;
        }
        using var context = refresh.OpenSession();
        EditInstance(root, "Refresh Prefab Instance", InteractionMode.AutomatedAction, () => context.RestoreHierarchy(CopyObject));
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
            var roots = scenes.Where(scene => !RuntimeAssetReferences.IsTransient(scene)).SelectMany(scene => scene.gameObjects)
                .Where(item => !RuntimeAssetReferences.IsTransient(item) && EditorPrefabEditSession.HasInstanceReference(item) &&
                    !HasPrefabAncestor(item)).ToArray();
            foreach (var root in roots)
            {
                if (root.isDestroyed) continue;
                try { PrefabUtility.RefreshPrefabInstance(root); }
                catch (Exception error) { Debug.LogError($"Could not refresh prefab '{root.name}': {error.Message}"); }
            }
            EditorPresentationRequests.NotifyPrefabsRefreshed();
        }
        finally { _refreshing = false; }
    }

    private static bool HasPrefabAncestor(GameObject item)
    {
        for (var parent = item.transform.parent; parent is not null; parent = parent.parent)
            if (EditorPrefabEditSession.HasInstanceReference(parent.gameObject)) return true;
        return false;
    }
}
