using BEngine.Build;
using BEngine.Rendering.Rhi;
using BEngine.Platform;

namespace BEngine.Editor;

/// <summary>Inputs understood by a platform publisher, independent of any content workflow.</summary>
public sealed class PlatformBuildRequest
{
    public PlatformBuildOptions Options { get; init; } = new();
    public string ProjectPath { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string Configuration { get; init; } = "Release";
    public bool SelfContained { get; init; } = true;
    public bool IncludeDebugSymbols { get; init; }
    public ResourcePackagingRequirements PackagingRequirements { get; init; } = ResourcePackagingRequirements.Empty;
    public string? PlayerHostProjectPath { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalMsBuildProperties { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
    public bool RuntimeShaderCompilation { get; init; } = true;
    public PlayerManagedStrippingLevel ManagedStripping { get; init; } = PlayerManagedStrippingLevel.Full;
    public string SplashBackgroundColor { get; init; } = "#10161A";
    public bool HttpStreamingAssets { get; init; }
    public string? EngineRuntimeRoot { get; init; }
}

/// <summary>A workflow supplies its layout; publishers do not choose content formats or paths.</summary>
public sealed record PlatformBuildLayout(
    string DataDirectoryName,
    string DataDirectory,
    string AssemblyDirectory,
    string ResourcesDirectory,
    string StreamingAssetsDirectory,
    string ExecutablePath);

public sealed class PlatformBuildContext(
    PlatformBuildRequest request,
    BuildTargetDescriptor target,
    string stagingDirectory,
    PlatformBuildLayout layout,
    IProgress<PlayerBuildProgress>? progress = null)
{
    public PlatformBuildRequest Request { get; } = request;
    public BuildTargetDescriptor Target { get; } = target;
    public string StagingDirectory { get; } = stagingDirectory;
    public PlatformBuildLayout Layout { get; } = layout;
    public IProgress<PlayerBuildProgress>? Progress { get; } = progress;
    public ReadOnlyMemory<byte> StartupLogoPng { get; init; }
    public ReadOnlyMemory<byte> ApplicationIconPng { get; init; }
    public IReadOnlyList<GraphicsBackend> GraphicsBackends { get; init; } = [];
    public IReadOnlyList<string> RuntimeManagedCodeAssemblyPaths { get; init; } = [];
    public IReadOnlyList<string> PrecompiledManagedCodeAssemblyPaths { get; init; } = [];
    public IReadOnlyList<string> PrecompiledManagedCodeReferencePaths { get; init; } = [];
    public IReadOnlyList<string> PrecompiledSharedManagedCodeAssemblyPaths { get; init; } = [];
    public bool IncludeTextBindings { get; set; } = true;
    public string GeneratedCodeNamespace { get; init; } = "BEngine.Generated";
    public string AdditionalSdkLinkerArguments { get; set; } = string.Empty;
    public PlatformBuildHooks Hooks { get; init; } = new();
}

/// <summary>Build composition points supplied by the engine adapter. Content formats may contribute packaged
/// image locations; platforms do not depend on their implementation.</summary>
public sealed class PlatformBuildHooks
{
    public Action<PlatformBuildContext, string, ProcessRunRequest>? ConfigurePublish { get; init; }
    public Action<PlatformBuildContext, string, ProcessRunResult>? Published { get; init; }
    public Func<PlatformPrecompiledAssemblyImage, string>? PrecompiledRegistrationStatement { get; init; }
    public Func<string, IReadOnlyCollection<string>, IReadOnlyDictionary<string, PlatformPackagedAssemblySource>>?
        PackagedAssemblySources { get; init; }
}

public sealed record PlatformPrecompiledAssemblyImage(string Name, string Sha256, string Mvid, bool Shared);

/// <summary>A validated managed image already present in a deployable archive.</summary>
public sealed record PlatformPackagedAssemblySource(string Path, int ArchiveSize, string ArchiveIntegrity,
    int Offset, int CompressedSize, int RawSize, string AssemblySha256, int Compression);

public interface IPlatformBuildTargetProvider
{
    string ProviderId { get; }
    bool CanBuild(BuildTargetDescriptor target, out string reason);
    Task BuildAsync(PlatformBuildContext context, CancellationToken cancellationToken = default);
}
