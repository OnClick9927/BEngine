using BEngine.Documents;

namespace BEngine.AssetBundles;

public sealed class AssetBundleSettingsDocument : Document
{
    public const string FileName = "AssetBundles.yaml";

    public string Format { get; set; } = "BEngine.AssetBundleSettings";
    public int Version { get; set; } = 1;
    public bool Enabled { get; set; }
    public string PackageName { get; set; } = "main";
    public string BuiltInDirectory { get; set; } = "StreamingAssets/AssetBundles";
    public string RemoteBaseUrl { get; set; } = string.Empty;
    public string CacheDirectory { get; set; } = string.Empty;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool ApplyUpdatesOnStartup { get; set; } = true;
    public bool FailStartupWhenUpdateFails { get; set; }
    public bool RequireHttps { get; set; } = true;
    public int MaxRetries { get; set; } = 3;
}
