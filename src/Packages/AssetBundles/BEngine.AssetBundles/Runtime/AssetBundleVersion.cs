using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleVersion
{
    public const string CurrentFormat = "BEngine.AssetBundleVersion";
    public const int CurrentSchemaVersion = 1;

    [YamlMember(Order = 0)] public string Format { get; set; } = CurrentFormat;
    [YamlMember(Order = 1)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [YamlMember(Order = 2)] public string PackageName { get; set; } = string.Empty;
    [YamlMember(Order = 3)] public string Version { get; set; } = "1.0.0";
    [YamlMember(Order = 4)] public string CatalogFile { get; set; } = "catalog.yaml";
    [YamlMember(Order = 5)] public string CatalogSha256 { get; set; } = string.Empty;
    [YamlMember(Order = 6)] public long CatalogSize { get; set; }

    public void Validate() => AssetBundleValidation.ValidateVersion(this);
}
