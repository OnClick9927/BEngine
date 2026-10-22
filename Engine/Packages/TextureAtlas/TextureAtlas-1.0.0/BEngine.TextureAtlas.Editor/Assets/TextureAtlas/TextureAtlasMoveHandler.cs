using static BEngine.Editor.AssetDatabase;
namespace BEngine.Editor;
public sealed class TextureAtlasMoveHandler : IAssetMoveHandler
{
    public bool CanMove(string path) => path.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase);
    public string Move(string oldPath, string newPath)
    {
        var host = EditorBridge.Host;
        if (host is null) return "No editor project is open.";
        var owner = host.GetAsset(oldPath);
        var atlasChild = owner is { ParentGuid: null } &&
                          oldPath.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase)
            ? host.FindAssets(string.Empty).FirstOrDefault(item =>
                item.ParentGuid == owner.Value.Guid &&
                item.LocalIdentifier == 2800000 &&
                item.AssetType.Equals(nameof(Texture), StringComparison.OrdinalIgnoreCase))
            : default;
        if (atlasChild.Guid != Guid.Empty && IsGeneratedArtifact(atlasChild, host))
        {
            var generatedMoveError = host.MoveAsset(oldPath, newPath);
            if (generatedMoveError.Length == 0)
            {
                BAsset.ClearLoadedAssets();
                ClearAssetCache();
                TextureAtlasResolver.Clear();
            }
            return generatedMoveError;
        }
        var outputPath = newPath.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase)
            ? newPath[..^".atlas.yaml".Length] + ".png"
            : ChangeExtension(newPath, ".png").Replace('\\', '/');
        var moveAtlasChild = atlasChild.Guid != Guid.Empty &&
                             !SameAssetPath(atlasChild.AssetPath, outputPath);
        string originalAtlasContents = string.Empty;
        if (atlasChild.Guid != Guid.Empty)
        {
            try
            {
                var oldFullPath = ResolveAssetPath(oldPath);
                originalAtlasContents = FileSystem.ReadAllText(oldFullPath);
                _ = TextureAtlas.Load(oldFullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               InvalidDataException or FormatException or
                                               YamlDotNet.Core.YamlException)
            {
                return $"The texture atlas cannot be moved because it is not readable: {exception.Message}";
            }

            if (moveAtlasChild && (host.GetAsset(outputPath) is not null ||
                                   FileSystem.FileExists(ResolveAssetPath(outputPath)) ||
                                   FileSystem.DirectoryExists(ResolveAssetPath(outputPath))))
                return $"The generated atlas Texture destination already exists: {outputPath}";
        }
        var moveError = host.MoveAsset(oldPath, newPath);
        if (moveError.Length > 0) return moveError;
        BAsset.ClearLoadedAssets();
        ClearAssetCache();
        if (atlasChild.Guid == Guid.Empty) return string.Empty;
        var childMoveError = moveAtlasChild ? host.MoveAsset(atlasChild.AssetPath, outputPath) : string.Empty;
        if (childMoveError.Length > 0)
        {
            var rollbackError = TryRollbackAssetMove(host, newPath, oldPath);
            BAsset.ClearLoadedAssets();
            ClearAssetCache();
            return rollbackError.Length == 0
                ? $"The generated atlas Texture could not be moved; the atlas move was rolled back: {childMoveError}"
                : $"The generated atlas Texture could not be moved: {childMoveError} " +
                  $"Rolling the atlas back also failed: {rollbackError}";
        }
        BAsset.ClearLoadedAssets();
        ClearAssetCache();
        try
        {
            var atlas = TextureAtlas.Load(ResolveAssetPath(newPath));
            atlas.Texture = outputPath;
            atlas.Save(ResolveAssetPath(newPath));
            host.ImportAsset(newPath);
            TextureAtlasResolver.Clear();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or FormatException or InvalidOperationException or
                                          YamlDotNet.Core.YamlException)
        {
            var childRollbackError = moveAtlasChild
                ? TryRollbackAssetMove(host, outputPath, atlasChild.AssetPath)
                : string.Empty;
            var atlasRollbackError = TryRollbackAssetMove(host, newPath, oldPath);
            var restoredPath = atlasRollbackError.Length == 0 ? oldPath : newPath;
            var restoreError = TryRestoreAtlasContents(host, restoredPath, originalAtlasContents);
            BAsset.ClearLoadedAssets();
            ClearAssetCache();
            TextureAtlasResolver.Clear();
            var rollbackErrors = new[] { childRollbackError, atlasRollbackError, restoreError }
                .Where(error => error.Length > 0).ToArray();
            return rollbackErrors.Length == 0
                ? $"The atlas Texture reference could not be updated; the move was rolled back: {exception.Message}"
                : $"The atlas Texture reference could not be updated: {exception.Message} " +
                  $"Rollback errors: {string.Join("; ", rollbackErrors)}";
        }
        return string.Empty;
    }
    private static string TryRestoreAtlasContents(IEditorHost host, string assetPath, string contents)
    {
        try
        {
            FileSystem.WriteAllText(ResolveAssetPath(assetPath), contents);
            host.ImportAsset(assetPath);
            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidDataException or InvalidOperationException)
        {
            return exception.Message;
        }
    }

}
