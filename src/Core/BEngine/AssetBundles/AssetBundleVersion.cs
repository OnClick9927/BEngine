using System.Text.Json.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleVersion
{
    public const string CurrentFormat = "BEngine.AssetBundleVersion";
    public const int CurrentSchemaVersion = 1;

    [JsonPropertyOrder(0)] public string Format { get; set; } = CurrentFormat;
    [JsonPropertyOrder(1)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [JsonPropertyOrder(2)] public string PackageName { get; set; } = string.Empty;
    [JsonPropertyOrder(3)] public string Version { get; set; } = "1.0.0";
    [JsonPropertyOrder(4)] public string CatalogFile { get; set; } = "catalog.json";
    [JsonPropertyOrder(5)] public string CatalogSha256 { get; set; } = string.Empty;
    [JsonPropertyOrder(6)] public long CatalogSize { get; set; }

    public void Validate() => AssetBundleValidation.ValidateVersion(this);
}
