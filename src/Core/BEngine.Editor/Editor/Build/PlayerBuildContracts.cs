using BEngine.Build;

namespace BEngine.Editor;

public sealed class PlayerBuildRequest
{
    public string ProjectPath { get; init; } = string.Empty;
    public string OutputDirectory { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Configuration { get; init; } = "Release";
    public bool DevelopmentBuild { get; init; }
    public bool SelfContained { get; init; } = true;
    public bool IncludeDebugSymbols { get; init; }
    public bool ReplaceExisting { get; init; }
    public string BuildVersion { get; init; } = "1.0.0";
    public string HotResourceVersion { get; init; } = "v1";
    public bool EnableHotUpdate { get; init; } = true;
    public PlayerContentUpdatePolicy ContentUpdatePolicy { get; init; } =
        PlayerContentUpdatePolicy.FallbackToLastValid;
    public bool CompressAssetBundles { get; init; } = true;
    public PlayerManagedStrippingLevel ManagedStripping { get; init; } =
        PlayerManagedStrippingLevel.Disabled;
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
    public IReadOnlyList<string> Scenes { get; init; } = [];
    public string? PlayerHostProjectPath { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalMsBuildProperties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record PlayerBuildResult(
    BuildTargetDescriptor Target,
    string OutputDirectory,
    string ContentVersion,
    TimeSpan Duration);

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
    internal IReadOnlyList<string> RuntimeManagedCodeAssemblyPaths { get; set; } = [];
}
