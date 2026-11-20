namespace BEngine.Editor;

public sealed class TextureAtlasMoveHandler : IAssetMoveHandler
{
    public bool CanMove(string path) => path.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase);

    public string Move(string oldPath, string newPath)
    {
        try
        {
            return AssetMoveTransaction.Execute(transaction =>
            {
                var child = transaction.FindFileSubAsset(oldPath, 2800000, nameof(Texture));
                if (child is { } generated && transaction.IsGeneratedArtifact(generated))
                {
                    transaction.Move(oldPath, newPath);
                    return;
                }
                // Legacy file-backed PNG naming and manifest edits are atlas policy.
                var outputPath = newPath.EndsWith(".atlas.yaml", StringComparison.OrdinalIgnoreCase)
                    ? newPath[..^".atlas.yaml".Length] + ".png"
                    : System.IO.Path.ChangeExtension(newPath, ".png").Replace('\\', '/');
                if (child is { } prior)
                {
                    _ = ReadAtlas(transaction.GetSourcePath(oldPath));
                    if (!transaction.GetSourcePath(prior.AssetPath).Equals(transaction.GetSourcePath(outputPath),
                            StringComparison.OrdinalIgnoreCase) && transaction.Exists(outputPath))
                        throw new IOException($"The generated atlas Texture destination already exists: {outputPath}");
                }
                transaction.Move(oldPath, newPath);
                if (child is not { } companion) return;
                transaction.Move(companion.AssetPath, outputPath);
                transaction.EditSource(newPath, path =>
                {
                    try
                    {
                        var atlas = ReadAtlas(path);
                        atlas.Texture = outputPath;
                        atlas.Save(path);
                    }
                    catch (YamlDotNet.Core.YamlException exception)
                    { throw new InvalidDataException("The atlas Texture reference could not be updated.", exception); }
                });
            });
        }
        finally { TextureAtlasResolver.Clear(); }
    }

    private static TextureAtlas ReadAtlas(string path)
    {
        try { return TextureAtlas.Load(path); }
        catch (YamlDotNet.Core.YamlException exception)
        { throw new InvalidDataException($"The texture atlas cannot be moved because it is not readable: {exception.Message}", exception); }
    }
}
