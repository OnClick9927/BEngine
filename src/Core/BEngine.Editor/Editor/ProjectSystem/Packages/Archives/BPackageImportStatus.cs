namespace BEngine.Editor;

public sealed class BPackageImportStatus
{
    internal BPackageImportStatus(
        BPackageImportState state,
        BPackageImportReceipt? receipt,
        IReadOnlyList<string> missingPaths,
        IReadOnlyList<string> modifiedPaths)
    {
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
