namespace BEngine.ProjectSystem.Editor;

public sealed partial class AssetDatabase
{
    /// <summary>Copies a file or folder into this project's Assets tree. Sources may
    /// be external. Metadata is not copied, so the next refresh assigns new identities.
    /// Returns an empty string on success. The caller owns refresh and UI policy.</summary>
    public string CopyAssetSource(string sourcePath, string targetPath) =>
        TransferSource(sourcePath, targetPath, move: false);

    /// <summary>Moves a source within this project's Assets tree, preserving metadata
    /// and GUIDs. Returns an empty string on success. The caller owns refresh and UI policy.</summary>
    public string MoveAssetSource(string sourcePath, string targetPath) =>
        TransferSource(sourcePath, targetPath, move: true);

    private string TransferSource(string sourcePath, string targetPath, bool move)
    {
        _refreshGate.Wait();
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
            var source = _pathService.GetFullPath(_pathService.Combine(_workspace.RootPath, sourcePath));
            var target = _pathService.GetFullPath(_pathService.Combine(_workspace.RootPath, targetPath));
            EnsureInsideAssets(target);
            if (move) EnsureInsideAssets(source);
            // Orphan metadata must not donate its previous identity to an incoming asset.
            if (_fileSystem.FileExists(target + ".meta"))
                return $"Destination metadata already exists: {target}.meta";
            var error = move
                ? AssetFileOperations.Move(source, target, _fileSystem, _pathService)
                : AssetFileOperations.Copy(source, target, _fileSystem, _pathService);
            // Even a failed file operation can leave partial filesystem changes.
            // An earlier background snapshot must not replace the post-operation view.
            Interlocked.Increment(ref _revision);
            return error;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            return error.Message;
        }
        finally { _refreshGate.Release(); }
    }
}
