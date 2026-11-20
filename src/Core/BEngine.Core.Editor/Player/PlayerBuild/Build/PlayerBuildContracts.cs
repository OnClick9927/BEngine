using BEngine.Hosting;
using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Build;

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
    public WebScriptCompilationMode WebScriptCompilation { get; init; } =
        WebScriptCompilationMode.Interpreter;
    public WebFontBackend WebFontBackend { get; init; } = WebFontBackend.Skia;
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
    /// <summary>Options interpreted by the selected content provider.</summary>
    public IReadOnlyDictionary<string, string> ContentOptions { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public PlayerManagedStrippingLevel ManagedStripping { get; init; } =
        PlayerManagedStrippingLevel.Full;
    public bool WritePlayerLog { get; init; } = true;
    public bool SplashScreenEnabled { get; init; } = true;
    public string SplashImage { get; init; } = string.Empty;
    public string SplashBackgroundColor { get; init; } = "#10161A";
    public float SplashMinimumDurationSeconds { get; init; } = 0.5f;
    public string CacheDirectory { get; init; } = HostDeploymentManifest.DefaultCacheDirectory;
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

public interface IPlayerBuildTargetProvider
{
    string ProviderId { get; }
    bool CanBuild(BuildTargetDescriptor target, out string reason);
    Task BuildAsync(PlayerBuildContext context, CancellationToken cancellationToken = default);
}

public sealed class PlayerBuildContext
{
    public PlayerBuildContext(
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
    public ResourcePackagingRequirements PackagingRequirements { get; internal set; } = ResourcePackagingRequirements.Empty;
    public string ContentVersion { get; set; } = string.Empty;
    /// <summary>Engine deployment policy, available during content finalization.</summary>
    public BEngine.Hosting.HostDeploymentManifest? DeploymentManifest { get; internal set; }
    public ReadOnlyMemory<byte> StartupLogoPng { get; set; }
    public ReadOnlyMemory<byte> ApplicationIconPng { get; set; }
    public IReadOnlyList<GraphicsBackend> GraphicsBackends { get; set; } = [];
    public string? HostContractPath { get; set; }
    public IReadOnlyList<string> RuntimeManagedCodeAssemblyPaths { get; set; } = [];
    public IReadOnlyList<string> WebPrecompiledManagedCodeAssemblyPaths { get; set; } = [];
    public IReadOnlyList<string> WebPrecompiledManagedCodeReferencePaths { get; set; } = [];
    public IReadOnlyList<string> WebPrecompiledSharedManagedCodeAssemblyPaths { get; set; } = [];
    public string? StrippingWorkDirectory { get; set; }
    public string? StrippingReportPath { get; set; }
    public PlayerStrippingReport? StrippingReport { get; set; }
    public bool IncludeTextBindings { get; set; } = true;
    public BEngine.Player.IPlayerContentKey? ContentEncryptionKey { get; set; }
    public string? ReflectionRootDescriptorPath { get; set; }
    public string? HostAssetRootDescriptorPath { get; set; }
}
