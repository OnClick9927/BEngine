namespace BEngine;

public sealed class AssetReferenceMetadata
{
    public string Guid { get; set; } = string.Empty;
    public Dictionary<string, string> Settings { get; set; } = [];
    public string ParentGuid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }
    public List<AssetReferenceSubAsset> SubAssets { get; set; } = [];
}

public sealed class AssetReferenceSubAsset
{
    public string Guid { get; set; } = string.Empty;
    public long LocalIdentifier { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
}
