using BEngine.Serialization;

namespace BEngine;

internal sealed class ManagedAssetDocumentCodec : IManagedAssetDocumentCodec
{
    public BObject Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var paths = RuntimeServices.GetRequiredService<Platform.PathUtility>();
        var files = RuntimeServices.GetRequiredService<Platform.FileUtility>();
        return ManagedAssetSerialization.DeserializeObject(files.ReadAllText(paths.GetFullPath(path)));
    }

    public void Save(BObject asset, string path)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(asset.isDestroyed, asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        DocumentFile.Write(path, () => ManagedAssetSerialization.Serialize(asset));
    }
}
