namespace BEngine.Serialization;

internal sealed class ManagedAssetData
{
    public string Format { get; set; } = "BEngine.ManagedAsset";
    public int Version { get; set; } = 1;
    public string TypeName { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public HideFlags HideFlags { get; set; }
    public ComponentGraphData? Graph { get; set; }

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

    internal BObject Create(Type type)
    {
        if (Format != "BEngine.ManagedAsset" || Version is < 1 or > 2 || !typeof(BObject).IsAssignableFrom(type))
            throw new InvalidDataException("Unsupported managed asset format or type.");
        if (Version == 1) return (BObject)YamlUtility.Deserialize(Data, type);
        if (Graph is null || !RuntimeTypeCache.TryCreateInstance(type, out var value) || value is not BObject asset)
            throw new InvalidDataException($"Cannot create managed asset '{type.FullName}'.");
        try
        {
            asset.name = Name; asset.hideFlags = HideFlags;
            ComponentObjectGraphSerializer.Restore(asset, Graph);
            SerializationCallbackUtility.AfterDeserialize(asset);
            return asset;
        }
        catch { BObject.DestroyImmediate(asset); throw; }
    }
}
