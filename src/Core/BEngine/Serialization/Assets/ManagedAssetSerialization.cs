namespace BEngine.Serialization;

/// <summary>Serializes native assets with the same typed object graph used by scenes.</summary>
public static class ManagedAssetSerialization
{
    public static Type GetAssetType(string yaml)
    {
        var document = YamlUtility.Deserialize<ManagedAssetData>(yaml);
        return BAssetReferenceLoader.ResolveType(document.TypeName)
            ?? throw new InvalidDataException($"Managed asset type '{document.TypeName}' is not loaded.");
    }

    public static string Serialize(BAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return YamlUtility.Serialize(ManagedAssetData.Capture(asset));
    }

    public static T Deserialize<T>(string yaml) where T : BAsset
    {
        var document = YamlUtility.Deserialize<ManagedAssetData>(yaml);
        var type = BAssetReferenceLoader.ResolveType(document.TypeName)
            ?? throw new InvalidDataException($"Managed asset type '{document.TypeName}' is not loaded.");
        if (!typeof(T).IsAssignableFrom(type)) throw new InvalidDataException($"Managed asset is not a {typeof(T).FullName}.");
        return (T)document.Create(type);
    }
}
