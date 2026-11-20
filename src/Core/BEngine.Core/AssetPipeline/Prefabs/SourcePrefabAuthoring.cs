namespace BEngine.Serialization;

internal static class SourcePrefabAuthoring
{
    internal static PrefabAsset SaveVariant(GameObject root, PrefabAsset basePrefab, string path, Guid? assetId = null)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(basePrefab);
        if (RuntimeAssetReferences.IsTransient(root))
            throw new InvalidOperationException("Runtime prefab instances cannot be saved as variants.");
        var codec = DependencyInjection.RuntimeServiceContributions.GetDefault<IPrefabDocumentCodec>() ??
            throw new InvalidOperationException("No prefab document codec is registered.");
        var data = PrefabDocumentSnapshot.Capture(root, assetId ?? codec.ReadAssetId(path) ?? Guid.NewGuid());
        if (data.Id == basePrefab.assetId) throw new InvalidOperationException("A variant cannot replace its base prefab.");
        var source = PrefabDocumentSnapshot.Resolve(basePrefab);
        var capturedIds = PrefabDocumentSnapshot.CaptureIdMap(root);
        var linkedIds = PrefabDocumentSnapshot.CaptureConnectionMap(root);
        var ids = new Dictionary<Guid, Guid>(linkedIds.Count);
        foreach (var pair in linkedIds) ids.Add(pair.Key, capturedIds.GetValueOrDefault(pair.Value, pair.Value));
        var address = DependencyInjection.RuntimeServiceContributions.GetDefault<IPrefabResolutionBackend>()?.Reference(basePrefab) ??
            (!string.IsNullOrEmpty(basePrefab.guid) ? $"guid:{basePrefab.guid}" : basePrefab.assetPath);
        var reference = PrefabReferenceSnapshot.Create(address, data.Root, ids);
        reference.SetBaseline(PrefabDocumentSnapshot.Map(source, reference, connected: false).Objects);
        data.SetBase(reference);
        var prefab = data.CreateAsset(path);
        codec.Save(prefab, path);
        BAsset.Invalidate(path);
        return prefab;
    }
}
