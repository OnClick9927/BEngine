using BEngine.ProjectSystem;

namespace BEngine.Editor;

public sealed class AssetBundleBuildOptions
{
    public string PackageName { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string OutputDirectory { get; init; } = string.Empty;
    public AssetBundleCompressionMode CompressionMode { get; init; } = AssetBundleCompressionMode.Optimal;
    public bool IncludeHotUpdateAssemblies { get; init; } = true;
    public bool IncludeManagedSymbols { get; init; } = true;
    public bool IncludePackageRuntimeResources { get; init; }
    internal RuntimeManagedCodeReleaseInputSet? ReleaseInputs { get; init; }
}
