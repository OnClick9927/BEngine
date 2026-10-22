using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleCatalog
{
    public const string CurrentFormat = "BEngine.AssetBundleCatalog";
    public const int MinimumSupportedSchemaVersion = 1;
    public const int CurrentSchemaVersion = 5;

    [YamlMember(Order = 0)] public string Format { get; set; } = CurrentFormat;
    [YamlMember(Order = 1)] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    [YamlMember(Order = 2)] public string PackageName { get; set; } = string.Empty;
    [YamlMember(Order = 3)] public string Version { get; set; } = "1.0.0";
    [YamlMember(Order = 4)] public List<AssetBundleDescriptor> Bundles { get; set; } = [];
    [YamlMember(Order = 5)] public List<AssetBundleAsset> Assets { get; set; } = [];
    [YamlMember(Order = 6)] public List<AssetBundlePackageDescriptor> Packages { get; set; } = [];
    /// <summary>A local catalog assembled by independent package updates.</summary>
    [YamlMember(Order = 7, DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool IsPackageComposite { get; set; }

    public void Validate() => AssetBundleValidation.ValidateCatalog(this);
}
