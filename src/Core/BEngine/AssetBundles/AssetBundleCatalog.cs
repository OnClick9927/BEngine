using System.Text.Json.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleCatalog
{
    public const string CurrentFormat = "BEngine.AssetBundleCatalog";
    public const int MinimumSupportedSchemaVersion = 1;
    public const int CurrentSchemaVersion = 3;

    [JsonPropertyOrder(0)] public string Format { get; set; } = CurrentFormat;
    [JsonPropertyOrder(1)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyOrder(2)] public string PackageName { get; set; } = string.Empty;
    [JsonPropertyOrder(3)] public string Version { get; set; } = "1.0.0";
    [JsonPropertyOrder(4)] public List<AssetBundleDescriptor> Bundles { get; set; } = [];
    [JsonPropertyOrder(5)] public List<AssetBundleAsset> Assets { get; set; } = [];

    public void Validate() => AssetBundleValidation.ValidateCatalog(this);
}
