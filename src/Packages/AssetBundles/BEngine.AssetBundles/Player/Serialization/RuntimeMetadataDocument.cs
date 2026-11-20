using BEngine.Hosting;
using BEngine.ProjectSystem;
using BEngine.AssetBundles;
using BEngine.Build;

namespace BEngine.Player.ProjectSystem;

public sealed class RuntimeMetadataDocument
{
    public const string DocumentFormat = "BEngine.RuntimeMetadata";
    public const int CurrentVersion = 3;

    public string Format { get; set; } = DocumentFormat;
    public int Version { get; set; } = CurrentVersion;
    public RuntimeProjectData Project { get; set; } = new();
    public RuntimeSettingsData ProjectSettings { get; set; } = new();
    public AssetBundleSettingsDocument AssetBundles { get; set; } = new();
    public HostDeploymentManifest? PlayerBootstrap { get; set; }
}
