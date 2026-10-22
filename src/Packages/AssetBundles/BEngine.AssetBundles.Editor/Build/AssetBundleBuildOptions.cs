using BEngine.ProjectSystem;

namespace BEngine.Editor;

public sealed class AssetBundleBuildOptions
{
    public BEngine.Content.ContentEncryptionKey? EncryptionKey { get; init; }
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public BEngine.Build.BuildTargetPlatform? TargetPlatform { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public AssetBundleCompressionMode CompressionMode { get; init; } = AssetBundleCompressionMode.Optimal;
    public bool IncludeHotUpdateAssemblies { get; init; } = true;
    public bool IncludeManagedSymbols { get; init; } = true;
    public bool IncludePackageRuntimeResources { get; init; }
    /// <summary>Stable build cache. Empty uses this project's Library/AssetBundleBuildCache.</summary>
    public string? CacheDirectory { get; init; }
    /// <summary>Recompress selected packages even when a verified cached artifact exists.</summary>
    public bool ForceRebuild { get; init; }
    /// <summary>
    /// Null builds all packages incrementally. Unselected packages must have unchanged,
    /// verified cached artifacts; otherwise the build fails before publication.
    /// </summary>
    public IReadOnlyList<string>? SelectedPackageNames { get; init; }
    internal RuntimeManagedCodeReleaseInputSet? ReleaseInputs { get; init; }
}
