using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleAsset
{
    [YamlMember(Order = 0)] public string Address { get; set; } = string.Empty;
    [YamlMember(Order = 1)] public Guid Guid { get; set; }
    [YamlMember(Order = 2, DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public Guid OwnerGuid { get; set; }
    [YamlMember(Order = 3, DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public long LocalIdentifier { get; set; }
    [YamlMember(Order = 4)] public string Bundle { get; set; } = string.Empty;
    [YamlMember(Order = 5)] public string Entry { get; set; } = string.Empty;
    [YamlMember(Order = 6)] public string AssetType { get; set; } = string.Empty;
    [YamlMember(Order = 7)] public string Sha256 { get; set; } = string.Empty;
    [YamlMember(Order = 8)] public long Size { get; set; }
    [YamlMember(Order = 9)] public string Importer { get; set; } = string.Empty;
    [YamlMember(Order = 10)] public Dictionary<string, string> ImporterSettings { get; set; } = [];

    [YamlIgnore]
    public bool IsSubAsset => LocalIdentifier > 0;
}
