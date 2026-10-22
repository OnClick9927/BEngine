namespace BEngine.ProjectSystem.Editor;

internal sealed class AssetRefreshSnapshot
{
    public AssetRefreshSnapshot(
        long baseRevision,
        IReadOnlyList<AssetRecord> records,
        IReadOnlyList<AssetChange> changes)
    {
        BaseRevision = baseRevision;
        Records = records;
        Changes = changes;
    }

    public long BaseRevision { get; }
    public IReadOnlyList<AssetRecord> Records { get; }
    public IReadOnlyList<AssetChange> Changes { get; }
}
