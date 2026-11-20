namespace BEngine.Serialization;

internal static class ManagedAssetDataOperations
{
    internal static ManagedAssetData Capture(BObject asset)
    {
        if (asset is BAsset persistent) persistent.EnsureAssetCanBeSaved();
        SerializationCallbackUtility.BeforeSerialize(asset);
        return new ManagedAssetData
        {
            Version = 2,
            TypeName = asset.GetType().AssemblyQualifiedName ?? asset.GetType().FullName ?? asset.GetType().Name,
            Name = asset.name,
            HideFlags = asset.hideFlags,
            Graph = ComponentObjectGraphSerializer.Capture(asset)
        };
    }

    internal static BObject Create(ManagedAssetData document, Type type)
    {
        if (document.Format != "BEngine.ManagedAsset" || document.Version is < 1 or > 2 ||
            !typeof(BObject).IsAssignableFrom(type))
            throw new InvalidDataException("Unsupported managed asset format or type.");
        if (document.Version == 1) return (BObject)YamlUtility.Deserialize(document.Data, type);
        if (document.Graph is null || !RuntimeTypeCache.TryCreateInstance(type, out var value) || value is not BObject asset)
            throw new InvalidDataException($"Cannot create managed asset '{type.FullName}'.");
        try
        {
            asset.name = document.Name; asset.hideFlags = document.HideFlags;
            ComponentObjectGraphSerializer.Restore(asset, document.Graph);
            SerializationCallbackUtility.AfterDeserialize(asset);
            return asset;
        }
        catch { BObject.DestroyImmediate(asset); throw; }
    }
}
