using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed class PlayerBuildRequest
{
    public string ProjectPath { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public IReadOnlyList<GraphicsBackend> GraphicsBackends { get; init; } = [];
    public bool CompileShaders { get; init; } = true;
    public bool RuntimeShaderCompilation { get; init; } = true;
    /// <summary>Actual Host contract from the main build targeted by this content update.</summary>
    public string HostContractPath { get; init; } = string.Empty;
    public string Configuration { get; init; } = "Release";
    public bool DevelopmentBuild { get; init; }
    public bool SelfContained { get; init; } = true;
    public PlayerGarbageCollectionMode WindowsGarbageCollection { get; init; } = PlayerGarbageCollectionMode.RuntimeDefault;
    public int WindowsGcHeapCount { get; init; }
    public bool IncludeDebugSymbols { get; init; }
    public bool EncryptContent { get; init; }
    /// <summary>Build-time secret; do not log or persist in project settings.</summary>
    public string EncryptionKey { get; init; } = string.Empty;
    public bool ReplaceExisting { get; init; }
    public string BuildVersion { get; init; } = "1.0.0";
    public string HotResourceVersion { get; init; } = "v1";
    public bool EnableHotUpdate { get; init; } = true;
    public PlayerContentUpdatePolicy ContentUpdatePolicy { get; init; } =
        PlayerContentUpdatePolicy.FallbackToLastValid;
    public bool CompressAssetBundles { get; init; } = true;
    public PlayerManagedStrippingLevel ManagedStripping { get; init; } =
        PlayerManagedStrippingLevel.Full;
    public bool WritePlayerLog { get; init; } = true;
    public bool SplashScreenEnabled { get; init; } = true;
    public string SplashImage { get; init; } = string.Empty;
    public string SplashBackgroundColor { get; init; } = "#10161A";
    public float SplashMinimumDurationSeconds { get; init; } = 1.5f;
    public string CacheDirectory { get; init; } = PlayerBootstrapManifest.DefaultCacheDirectory;
    public string AndroidApplicationIdentifier { get; init; } = "com.bengine.game";
    public int AndroidMinimumApiLevel { get; init; } = 26;
    public bool AndroidBuildAppBundle { get; init; } = true;
    public string IosBundleIdentifier { get; init; } = "com.bengine.game";
    public string IosMinimumVersion { get; init; } = "15.0";
    public string IosCodesignKey { get; init; } = string.Empty;
    public string IosProvisioningProfile { get; init; } = string.Empty;
    public string HotUpdateStartupScene { get; init; } = string.Empty;
    public IReadOnlyList<string> Scenes { get; init; } = [];
    public string? PlayerHostProjectPath { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalMsBuildProperties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record PlayerBuildResult(
    BuildTargetDescriptor Target,
    string OutputDirectory,
    string ContentVersion,
    TimeSpan Duration)
{
    public string? StrippingReportPath { get; init; }
    public string? HostContractPath { get; init; }
    public string? ContentEncryptionKeyFingerprint { get; init; }
}

public enum PlayerBuildPhase
{
    Preparing,
    CompilingPlayer,
    BuildingContent,
    Publishing,
    Completed
}

public sealed record PlayerBuildProgress(
    PlayerBuildPhase Phase,
    string Message,
    float Progress);

public interface IPlayerBuildTargetProvider
{
    string ProviderId { get; }
    bool CanBuild(BuildTargetDescriptor target, out string reason);
    Task BuildAsync(PlayerBuildContext context, CancellationToken cancellationToken = default);
}

public sealed record PlayerBuildPrerequisite(
    string Id,
    bool IsSatisfied,
    string Message,
    string? Remediation = null);

public interface IPlayerBuildPrerequisiteProvider
{
    bool SupportsTarget(BuildTargetDescriptor target);
    IReadOnlyList<PlayerBuildPrerequisite> GetPrerequisites(BuildTargetDescriptor target);
}

public sealed class PlayerBuildContext
{
    internal PlayerBuildContext(
        PlayerBuildRequest request,
        BuildTargetDescriptor target,
        string stagingDirectory,
        IProgress<PlayerBuildProgress>? progress)
    {
        Request = request;
        Target = target;
        StagingDirectory = stagingDirectory;
        Progress = progress;
    }

    public PlayerBuildRequest Request { get; }
    public BuildTargetDescriptor Target { get; }
    public string StagingDirectory { get; }
    public IProgress<PlayerBuildProgress>? Progress { get; }
    public string ContentVersion { get; internal set; } = string.Empty;
    public IReadOnlyList<GraphicsBackend> GraphicsBackends { get; internal set; } = [];
    public string? HostContractPath { get; internal set; }
    internal IReadOnlyList<string> RuntimeManagedCodeAssemblyPaths { get; set; } = [];
    internal string? StrippingWorkDirectory { get; set; }
    internal string? StrippingReportPath { get; set; }
    internal PlayerStrippingReport? StrippingReport { get; set; }
    internal bool IncludeTextBindings { get; set; } = true;
    internal BEngine.Content.ContentEncryptionKey? ContentEncryptionKey { get; set; }
    internal string? ReflectionRootDescriptorPath { get; set; }
}
