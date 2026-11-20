namespace BEngine.Editor;

public sealed class BPackageImportStatus
{
    /// <summary>Creates the inspection result returned by an archive service provider.</summary>
    public BPackageImportStatus(
        BPackageImportState state,
        BPackageImportReceipt? receipt,
        IReadOnlyList<string> missingPaths,
        IReadOnlyList<string> modifiedPaths)
    {
        ArgumentNullException.ThrowIfNull(missingPaths);
        ArgumentNullException.ThrowIfNull(modifiedPaths);
        State = state;
        Receipt = receipt;
        MissingPaths = missingPaths;
        ModifiedPaths = modifiedPaths;
    }

    public BPackageImportState State { get; }
    public BPackageImportReceipt? Receipt { get; }
    public IReadOnlyList<string> MissingPaths { get; }
    public IReadOnlyList<string> ModifiedPaths { get; }
    public bool IsInstalled => State != BPackageImportState.NotInstalled;
    public bool IsTracked => Receipt is not null;
}
