using BEngine.AssetBundles;
using BEngine.Build;

namespace BEngine.ProjectSystem;

public sealed class RuntimeMetadataDocument
{
    public const string DocumentFormat = "BEngine.RuntimeMetadata";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = DocumentFormat;
    public int Version { get; set; } = CurrentVersion;
    public ProjectData Project { get; set; } = new();
    public ProjectSettingsData ProjectSettings { get; set; } = new();
    public AssetBundleSettingsDocument AssetBundles { get; set; } = new();
    public List<RuntimePackageReferenceData> EnabledPackages { get; set; } = [];
    public PlayerBootstrapManifest? PlayerBootstrap { get; set; }
}
