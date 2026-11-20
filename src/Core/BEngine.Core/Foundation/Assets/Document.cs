namespace BEngine;

/// <summary>
/// Source/Artifact bridge for one concrete asset type.
/// Persistence stays outside BAsset and runtime-only objects can never be written.
/// </summary>
public sealed class Document<TAsset> where TAsset : BAsset
{
    private readonly TAsset _asset;

    private Document(TAsset asset) =>
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));

    public static Document<TAsset> Read(string path, Func<string, TAsset> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(reader);
        var fullPath = RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>().GetFullPath(path);
        var asset = reader(fullPath) ??
            throw new InvalidDataException($"The {typeof(TAsset).Name} document is empty.");
        RuntimeAssetReferences.BindSource(asset, path, fullPath, fullPath,
            asset is Scene or PrefabAsset ? asset.Id : RuntimeAssetReferences.ResolveIdentity(fullPath, typeof(TAsset)), typeof(TAsset).Name);
        return new Document<TAsset>(asset);
    }

    public static Document<TAsset> Parse(string contents, Func<string, TAsset> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contents);
        ArgumentNullException.ThrowIfNull(reader);
        return new Document<TAsset>(reader(contents) ??
            throw new InvalidDataException($"The {typeof(TAsset).Name} document is empty."));
    }

    public static Document<TAsset> FromAsset(TAsset asset) => new(asset);

    public TAsset ToAsset() => _asset;

    public string Serialize(Func<TAsset, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer(_asset);
    }

    public void Write(string path, Action<TAsset, string> writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writer);
        EnsureEditorAsset(_asset);
        ObjectDisposedException.ThrowIf(_asset.isDestroyed, _asset);
        var fullPath = RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>().GetFullPath(path);
        RuntimeAssetReferences.BindSource(_asset, path, fullPath, fullPath,
            _asset is Scene or PrefabAsset ? _asset.Id : RuntimeAssetReferences.ResolveIdentity(fullPath, typeof(TAsset)), typeof(TAsset).Name);
        writer(_asset, fullPath);
        BAsset.Invalidate(path);
    }

    private static void EnsureEditorAsset(TAsset asset)
    {
        if (RuntimeAssetReferences.IsTransient(asset))
            throw new InvalidOperationException("Runtime assets are transient and cannot be saved.");
    }
}
