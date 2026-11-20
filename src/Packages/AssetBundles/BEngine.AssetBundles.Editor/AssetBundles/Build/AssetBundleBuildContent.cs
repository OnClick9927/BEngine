namespace BEngine.Editor;

/// <summary>A generic payload supplied by the caller, independently of project importers.</summary>
public sealed class AssetBundleBuildContent
{
    public required string Address { get; init; }
    public required string AssetType { get; init; }
    public required string Importer { get; init; }
    public IReadOnlyDictionary<string, string>? ImporterSettings { get; init; }
    /// <summary>Exactly one of FilePath and Content must be supplied.</summary>
    public string? FilePath { get; init; }
    /// <summary>Cloned during input capture, before asynchronous archive construction.</summary>
    public byte[]? Content { get; init; }
    /// <summary>Optional caller snapshot to verify before building.</summary>
    public string? ExpectedSha256 { get; init; }
    public long? ExpectedSize { get; init; }
}

/// <summary>A caller-provided bundle; participates in package selection and incremental builds.</summary>
public sealed class AssetBundleBuildContentBundle
{
    public required string Name { get; init; }
    public required string PackageName { get; init; }
    public required IReadOnlyList<AssetBundleBuildContent> Assets { get; init; }
}
