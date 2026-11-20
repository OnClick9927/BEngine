using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>A prepared source merge, with private graph data. Applying it requires the caller's edit transaction.</summary>
public sealed class EditorPrefabRefresh
{
    private readonly GameObject _root;
    private readonly SceneAssetData _desired;
    public bool HasChanges { get; }
    public bool MetadataOnly { get; }

    private EditorPrefabRefresh(GameObject root, SceneAssetData desired, bool changed, bool metadataOnly)
    { _root = root; _desired = desired; HasChanges = changed; MetadataOnly = metadataOnly; }

    public static EditorPrefabRefresh? Prepare(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.PrefabInstance is null) return null;
        var before = EditorPrefabEditSession.CaptureHierarchy(root).Data;
        var references = PrefabAssetOperations.Traverse(root).Where(item => item.PrefabInstance is not null)
            .Select(item => AssetSnapshotClone.Clone(item.PrefabInstance!)).ToList();
        var desired = new SceneAssetData
        {
            Id = Guid.NewGuid(), Name = root.name, GameObjects = AssetSnapshotClone.Clone(before),
            PrefabInstances = AssetSnapshotClone.Clone(references)
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
        var sameObjects = AssetSnapshotComparison.Equal(before.OrderBy(item => item.Id).ToArray(), desired.GameObjects.OrderBy(item => item.Id).ToArray());

        var changed = !sameObjects || !AssetSnapshotComparison.Equal(references, desired.PrefabInstances);
        return new(root, desired, changed, changed && sameObjects && root.PrefabAssetId is null);
    }

    /// <summary>Advances provenance only, without marking a just-saved authoring stage dirty.</summary>
    public void ApplyMetadata()
    {
        if (!MetadataOnly) throw new InvalidOperationException("This refresh requires a hierarchy edit transaction.");
        var byRoot = _desired.PrefabInstances.ToDictionary(item => item.Root);
        foreach (var item in PrefabAssetOperations.Traverse(_root))
            item.PrefabInstance = byRoot.TryGetValue(item.Id, out var reference) ? AssetSnapshotClone.Clone(reference) : null;
    }

    /// <summary>Creates owned detached contents, preserving external scene references and live identities.</summary>
    public EditorPrefabEditSession OpenSession()
    {
        if (!HasChanges || MetadataOnly) throw new InvalidOperationException("This refresh does not require a hierarchy edit.");
        return new(_root, _desired);
    }
}
