namespace BEngine;

/// <summary>
/// Internal Source/Artifact bridge for one concrete asset type.
/// Persistence stays outside BAsset and runtime-only objects can never be written.
/// </summary>
internal sealed class Document<TAsset> where TAsset : BAsset
{
    private readonly TAsset _asset;

    private Document(TAsset asset) =>
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));

    internal static Document<TAsset> Read(string path, Func<string, TAsset> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(reader);
        var fullPath = RuntimeServiceAccess.PathService.GetFullPath(path);
        var asset = reader(fullPath) ??
            throw new InvalidDataException($"The {typeof(TAsset).Name} document is empty.");
        asset.BindAssetFile(path, fullPath, asset is Scene or PrefabAsset ? asset.Id : BAssetReferenceLoader.ResolveIdentity(fullPath, typeof(TAsset)), typeof(TAsset).Name);
        return new Document<TAsset>(asset);
    }

    internal static Document<TAsset> Parse(string contents, Func<string, TAsset> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contents);
        ArgumentNullException.ThrowIfNull(reader);
        return new Document<TAsset>(reader(contents) ??
            throw new InvalidDataException($"The {typeof(TAsset).Name} document is empty."));
    }

    internal static Document<TAsset> FromAsset(TAsset asset) => new(asset);

    internal TAsset ToAsset() => _asset;

    internal string Serialize(Func<TAsset, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return writer(_asset);
    }

    internal void Write(string path, Action<TAsset, string> writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(writer);
        EnsureEditorAsset(_asset);
        ObjectDisposedException.ThrowIf(_asset.isDestroyed, _asset);
        var fullPath = RuntimeServiceAccess.PathService.GetFullPath(path);
        _asset.BindAssetFile(path, fullPath, _asset is Scene or PrefabAsset ? _asset.Id : BAssetReferenceLoader.ResolveIdentity(fullPath, typeof(TAsset)), typeof(TAsset).Name);
        writer(_asset, fullPath);
        BAsset.Invalidate(path);
    }

    private static void EnsureEditorAsset(TAsset asset)
    {
        if (asset.IsRuntimeOnly || asset is Scene scene &&
            (scene.IsRuntimeOnly || SceneRuntime.IsRunningScene(scene)))
            throw new InvalidOperationException("Runtime assets are transient and cannot be saved.");
    }
}
