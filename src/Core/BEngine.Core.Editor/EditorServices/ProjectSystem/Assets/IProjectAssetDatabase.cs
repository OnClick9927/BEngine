namespace BEngine.ProjectSystem.Editor;

/// <summary>Authoring asset access independent of the scanning and import implementation.</summary>
public interface IProjectAssetDatabase
{
    AssetHashStatistics lastHashStatistics { get; }
    event Action<IReadOnlyList<AssetChange>>? assetsChanged;
    IReadOnlyCollection<AssetRecord> assets { get; }
    IReadOnlyList<AssetChange> Refresh(Action<AssetScanProgress>? progress = null);
    IReadOnlyList<AssetChange> RefreshRegisteredTypes(Action<AssetScanProgress>? progress = null);
    AssetRefreshSnapshot PrepareRefresh(Action<AssetScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
    IReadOnlyList<AssetChange> ApplyRefresh(AssetRefreshSnapshot snapshot);
    bool TryApplyRefresh(AssetRefreshSnapshot snapshot, out IReadOnlyList<AssetChange> changes);
    AssetRecord ImportAsset(string path);
    /// <summary>Copies source data into project Assets without importing. Empty means
    /// success; the caller owns refresh and UI policy. Read-only providers may decline.</summary>
    string CopyAssetSource(string sourcePath, string targetPath) => "This asset provider does not support source copies.";
    /// <summary>Moves project source data and metadata without importing. Empty means
    /// success; the caller owns refresh and UI policy. Read-only providers may decline.</summary>
    string MoveAssetSource(string sourcePath, string targetPath) => "This asset provider does not support source moves.";
    string? AssetPathToGuid(string assetPath);
    string? GuidToAssetPath(Guid guid);
    AssetRecord? GetRecord(Guid guid);
    AssetRecord? GetRecord(string assetPath);
    IReadOnlyList<AssetRecord> FindAssets(string search);
}

public readonly record struct AssetHashStatistics(long FileReads, long BytesRead, long CacheHits);
