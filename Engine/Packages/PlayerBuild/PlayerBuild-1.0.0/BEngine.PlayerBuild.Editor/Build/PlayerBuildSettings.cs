using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed class PlayerBuildSettings
{
    public const string CurrentFormat = "BEngine.BuildSettings";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public List<PlayerBuildScene> Scenes { get; set; } = [];
    public string HotUpdateStartupScene { get; set; } = string.Empty;
    public List<string> ScriptingDefineSymbols { get; set; } = [];
    public string TargetId { get; set; } = "windows";
    public List<GraphicsBackend> GraphicsBackends { get; set; } = [];
    public bool CompileShaders { get; set; } = true;
    public bool RuntimeShaderCompilation { get; set; } = true;
    public string HostContractPath { get; set; } = string.Empty;
    public bool DevelopmentBuild { get; set; }
    public bool IncludeDebugSymbols { get; set; }
    public bool EncryptContent { get; set; }
    [YamlDotNet.Serialization.YamlIgnore]
    public string EncryptionKey { get; set; } = string.Empty;
    public bool SelfContained { get; set; } = true;
    public PlayerGarbageCollectionMode WindowsGarbageCollection { get; set; } = PlayerGarbageCollectionMode.RuntimeDefault;
    public int WindowsGcHeapCount { get; set; }
    public string BuildVersion { get; set; } = "1.0.0";
    public string HotResourceVersion { get; set; } = "v1";
    public bool EnableHotUpdate { get; set; } = true;
    public PlayerContentUpdatePolicy ContentUpdatePolicy { get; set; } =
        PlayerContentUpdatePolicy.FallbackToLastValid;
    public bool CompressAssetBundles { get; set; } = true;
    public PlayerManagedStrippingLevel ManagedStripping { get; set; } =
        PlayerManagedStrippingLevel.Full;
    public bool WritePlayerLog { get; set; } = true;
    public bool SplashScreenEnabled { get; set; } = true;
    public string SplashImage { get; set; } = string.Empty;
    public string SplashBackgroundColor { get; set; } = "#10161A";
    public float SplashMinimumDurationSeconds { get; set; } = 1.5f;
    public string CacheDirectory { get; set; } = PlayerBootstrapManifest.DefaultCacheDirectory;
    public string AndroidApplicationIdentifier { get; set; } = "com.bengine.game";
    public int AndroidMinimumApiLevel { get; set; } = 26;
    public bool AndroidBuildAppBundle { get; set; } = true;
    public string IosBundleIdentifier { get; set; } = "com.bengine.game";
    public string IosMinimumVersion { get; set; } = "15.0";
    public string IosCodesignKey { get; set; } = string.Empty;
    public string IosProvisioningProfile { get; set; } = string.Empty;
}
