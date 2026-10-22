namespace BEngine.Editor;

public sealed class BPackageImportResult
{
    internal BPackageImportResult(
        BPackageManifest manifest,
        IReadOnlyList<string> importedPaths,
        IReadOnlyList<string> skippedPaths)
        : this(manifest, importedPaths, skippedPaths, [], [], [], null)
    {
    }

    internal BPackageImportResult(
        BPackageManifest manifest,
        IReadOnlyList<string> importedPaths,
        IReadOnlyList<string> skippedPaths,
        IReadOnlyList<string> unchangedPaths,
        IReadOnlyList<string> preservedModifiedPaths,
        IReadOnlyList<string> removedPaths,
        BPackageImportReceipt? receipt)
    {
        Manifest = manifest;
        ImportedPaths = importedPaths;
        SkippedPaths = skippedPaths;
        UnchangedPaths = unchangedPaths;
        PreservedModifiedPaths = preservedModifiedPaths;
        RemovedPaths = removedPaths;
        Receipt = receipt;
    }

    public BPackageManifest Manifest { get; }
    public IReadOnlyList<string> ImportedPaths { get; }
    public IReadOnlyList<string> SkippedPaths { get; }
    public IReadOnlyList<string> UnchangedPaths { get; }
    public IReadOnlyList<string> PreservedModifiedPaths { get; }
    public IReadOnlyList<string> RemovedPaths { get; }
    public BPackageImportReceipt? Receipt { get; }
}
