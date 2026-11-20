namespace BEngine.Serialization;

/// <summary>Serializes native assets with the same typed object graph used by scenes.</summary>
public static class ManagedAssetSerialization
{
    /// <summary>Reads a stored managed-object type identity without resolving or creating it.
    /// Returns null for an unrecognized format or an empty identity. Invalid YAML propagates.</summary>
    public static string? GetSerializedTypeName(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        var header = YamlUtility.Deserialize<SerializedTypeHeader>(yaml);
        return header.Format == "BEngine.ManagedAsset" && !string.IsNullOrWhiteSpace(header.TypeName)
            ? header.TypeName : null;
    }

    private sealed class SerializedTypeHeader
    {
        // Legacy source documents may omit the format field, matching ManagedAssetData.
        public string Format { get; set; } = "BEngine.ManagedAsset";
        public string TypeName { get; set; } = string.Empty;
    }

    public static Type GetAssetType(string yaml)
    {
        var document = YamlUtility.Deserialize<ManagedAssetData>(yaml);
        return BAssetReferenceLoader.ResolveType(document.TypeName)
            ?? throw new InvalidDataException($"Managed asset type '{document.TypeName}' is not loaded.");
    }

    public static string Serialize(BAsset asset) => Serialize((BObject)asset);

    /// <summary>Captures native assets and embedded managed objects without choosing a storage location.</summary>
    public static string Serialize(BObject asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return YamlUtility.Serialize(ManagedAssetDataOperations.Capture(asset));
    }

    /// <summary>Restores a native managed object, including legacy version-one documents.
    /// Storage adapters bind the imported identity after decoding.</summary>
    public static BObject DeserializeObject(string yaml)
    {
        var document = YamlUtility.Deserialize<ManagedAssetData>(yaml);
        var type = RuntimeTypeCache.ResolveSerializedType(document.TypeName)
            ?? throw new InvalidDataException($"Managed asset type '{document.TypeName}' is not loaded.");
        return ManagedAssetDataOperations.Create(document, type);
    }

    public static T Deserialize<T>(string yaml) where T : BAsset
    {
        var document = YamlUtility.Deserialize<ManagedAssetData>(yaml);
        var type = BAssetReferenceLoader.ResolveType(document.TypeName)
            ?? throw new InvalidDataException($"Managed asset type '{document.TypeName}' is not loaded.");
        if (!typeof(T).IsAssignableFrom(type)) throw new InvalidDataException($"Managed asset is not a {typeof(T).FullName}.");
        return (T)ManagedAssetDataOperations.Create(document, type);
    }
}
