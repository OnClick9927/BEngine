using YamlDotNet.Serialization;

namespace BEngine;

public abstract class BAsset : BObject
{
    public virtual IReadOnlyList<BAsset> GetImportedRepresentations() => [];

    /// <summary>Exports private asset state for an isolated editor runtime mirror. Payloads must use
    /// CLR/core types so another loaded version of this asset's assembly can restore them.</summary>
    protected virtual object? CaptureClonePayload() => null;

    /// <summary>Restores a private payload into a newly constructed asset. Use cloneObject for owned
    /// or borrowed BObject references to preserve sharing within the isolated object graph.</summary>
    protected virtual void RestoreClonePayload(object? payload, Func<BObject, BObject> cloneObject)
    {
        if (payload is not null) throw new NotSupportedException($"{GetType().FullName} cannot restore this asset payload version.");
    }

    internal void CopyClonePayloadFrom(BAsset source, Func<BObject, BObject> cloneObject)
    {
        source.ThrowIfDestroyed();
        RestoreClonePayload(source.CaptureClonePayload(), cloneObject);
    }

    internal virtual void MarkOwnedAssetsRuntimeOnly() { }

    // Called exactly once when explicitly destroyed or unloaded; borrowed references are not owned.
    protected virtual void ReleaseAssetResources() { }

    internal void DestroyAsset()
    {
        if (isDestroyed) return;
        BAssetReferenceLoader.Unload(this);
        Unregister(this);
        ReleaseAssetResources();
    }

    protected void ThrowIfDestroyed() => ObjectDisposedException.ThrowIf(isDestroyed, this);

    protected internal void EnsureAssetCanBeSaved()
    {
        ThrowIfDestroyed();
        if (IsRuntimeOnly || this is Scene scene && SceneRuntime.IsRunningScene(scene))
            throw new InvalidOperationException("Runtime assets are transient and cannot be saved.");
    }

    internal static long CacheGeneration => BAssetReferenceLoader.Generation;
    /// <summary>Changes when cached asset resolutions are invalidated.</summary>
    public static long cacheGeneration => CacheGeneration;
    [YamlIgnore, HideInInspector]
    public string assetPath { get; protected internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    public string guid { get; internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    public string sourcePath { get; internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    public string artifactPath { get; internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    public string assetType { get; internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    internal Guid? parentAssetGuid { get; private set; }

    [YamlIgnore, HideInInspector]
    internal long localIdentifier { get; private set; }

    public static TAsset? Load<TAsset>(string path) where TAsset : BAsset =>
        Load(path, typeof(TAsset)) as TAsset;

    public static BAsset? Load(string path, Type assetType) => BAssetReferenceLoader.Load(path, assetType);

    public static TAsset? LoadSubAsset<TAsset>(string path, long localIdentifier) where TAsset : BAsset =>
        BAssetReferenceLoader.LoadSubAsset(path, localIdentifier, typeof(TAsset)) as TAsset;

    public static BAsset? LoadSubAsset(string path, long localIdentifier, Type assetType) =>
        BAssetReferenceLoader.LoadSubAsset(path, localIdentifier, assetType);

    public static TAsset? LoadByGuid<TAsset>(string guid) where TAsset : BAsset =>
        BAssetReferenceLoader.LoadByGuid(guid, typeof(TAsset)) as TAsset;

    public static BAsset? LoadByGuid(string guid, Type assetType) =>
        BAssetReferenceLoader.LoadByGuid(guid, assetType);

    public static void Invalidate(string path) => BAssetReferenceLoader.Invalidate(path);

    public static void ClearLoadedAssets() => BAssetReferenceLoader.Clear();

    protected internal void BindAssetReference(string projectPath, Guid? id = null)
    {
        var normalized = projectPath?.Trim() ?? string.Empty;
        if (normalized.Length > 0 && RuntimeServiceAccess.PathService.IsPathRooted(normalized))
            normalized = AssetReferencePath.ToReference(normalized);
        assetPath = normalized.Replace('\\', '/');
        if (assetPath.Length > 0 && (string.IsNullOrWhiteSpace(name) || name == GetType().Name))
            name = RuntimeServiceAccess.PathService.GetFileNameWithoutExtension(assetPath);
        parentAssetGuid = null;
        localIdentifier = 0;
        if (id is not { } value) return;
        Id = value;
        if (this is PrefabAsset prefab) prefab.Data.Id = value;
        guid = value.ToString("N");
    }

    internal void BindAssetFile(
        string projectPath,
        string sourcePhysicalPath,
        string artifactPhysicalPath,
        Guid id,
        string type)
    {
        BindAssetReference(projectPath, id);
        sourcePath = sourcePhysicalPath ?? string.Empty;
        artifactPath = artifactPhysicalPath ?? string.Empty;
        assetType = type ?? string.Empty;
    }

    internal void BindAssetFile(string projectPath, string physicalPath, Guid id, string type) =>
        BindAssetFile(projectPath, physicalPath, physicalPath, id, type);

    // A runtime mirror keeps its own object identity, but retains its source reference.
    internal void CopyAssetReferenceFrom(BAsset source)
    {
        assetPath = source.assetPath;
        guid = source.guid;
        sourcePath = source.sourcePath;
        artifactPath = source.artifactPath;
        assetType = source.assetType;
        parentAssetGuid = source.parentAssetGuid;
        localIdentifier = source.localIdentifier;
    }

    internal void BindSubAssetReference(
        string projectPath,
        Guid parentGuid,
        long localId,
        Guid? objectId = null)
    {
        var normalized = projectPath?.Trim() ?? string.Empty;
        if (normalized.Length > 0 && RuntimeServiceAccess.PathService.IsPathRooted(normalized))
            normalized = AssetReferencePath.ToReference(normalized);
        assetPath = normalized.Replace('\\', '/');
        parentAssetGuid = parentGuid;
        localIdentifier = localId;
        if (objectId is { } value) Id = value;
        guid = parentGuid.ToString("N");
    }
}
