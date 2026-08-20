using System.Text.Json.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleDescriptor
{
    [JsonPropertyOrder(0)] public string Name { get; set; } = string.Empty;
    [JsonPropertyOrder(1)] public string FileName { get; set; } = string.Empty;
    [JsonPropertyOrder(2)] public string Sha256 { get; set; } = string.Empty;
    [JsonPropertyOrder(3)] public long Size { get; set; }
    [JsonPropertyOrder(4)] public List<string> Dependencies { get; set; } = [];
}
