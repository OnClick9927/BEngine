using System.Text.Json.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleAsset
{
    [JsonPropertyOrder(0)] public string Address { get; set; } = string.Empty;
    [JsonPropertyOrder(1)] public Guid Guid { get; set; }
    [JsonPropertyOrder(2)] public string Bundle { get; set; } = string.Empty;
    [JsonPropertyOrder(3)] public string Entry { get; set; } = string.Empty;
    [JsonPropertyOrder(4)] public string AssetType { get; set; } = string.Empty;
    [JsonPropertyOrder(5)] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyOrder(6)] public long Size { get; set; }
    [JsonPropertyOrder(7)] public string Importer { get; set; } = string.Empty;
    [JsonPropertyOrder(8)] public Dictionary<string, string> ImporterSettings { get; set; } = [];
}
