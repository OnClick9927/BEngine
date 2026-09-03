using BEngine.Build;

namespace BEngine.Editor;

public sealed class PlayerBuildSettings
{
    public const string CurrentFormat = "BEngine.BuildSettings";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public List<PlayerBuildScene> Scenes { get; set; } = [];
    public string TargetId { get; set; } = "windows";
    public bool DevelopmentBuild { get; set; }
    public bool IncludeDebugSymbols { get; set; }
    public bool SelfContained { get; set; } = true;
    public string BuildVersion { get; set; } = "1.0.0";
    public string HotResourceVersion { get; set; } = "v1";
    public bool EnableHotUpdate { get; set; } = true;
    public PlayerContentUpdatePolicy ContentUpdatePolicy { get; set; } =
        PlayerContentUpdatePolicy.FallbackToLastValid;
    public bool CompressAssetBundles { get; set; } = true;
    public PlayerManagedStrippingLevel ManagedStripping { get; set; } =
        PlayerManagedStrippingLevel.Disabled;
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
}
