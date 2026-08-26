using YamlDotNet.Serialization;

namespace BEngine;

public abstract class BAsset : BObject
{
    [YamlIgnore, HideInInspector]
    public string assetPath { get; protected internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    public string guid { get; internal set; } = string.Empty;

    public static TAsset? Load<TAsset>(string path) where TAsset : BAsset =>
        Load(path, typeof(TAsset)) as TAsset;

    public static BAsset? Load(string path, Type assetType) => BAssetReferenceLoader.Load(path, assetType);

    public static void Invalidate(string path) => BAssetReferenceLoader.Invalidate(path);

    public static void ClearLoadedAssets() => BAssetReferenceLoader.Clear();

    protected internal void BindAssetReference(string projectPath, Guid? id = null)
    {
        var normalized = projectPath?.Trim() ?? string.Empty;
        if (normalized.Length > 0 && Path.IsPathRooted(normalized))
            normalized = AssetReferencePath.ToReference(normalized);
        assetPath = normalized.Replace('\\', '/');
        if (id is not { } value) return;
        Id = value;
        guid = value.ToString("N");
    }
}

/// <summary>Base class for assets backed by a project or package file.</summary>
public abstract class FileAsset : BAsset
{
    [YamlIgnore, HideInInspector]
    public string sourcePath { get; internal set; } = string.Empty;

    [YamlIgnore, HideInInspector]
    public string assetType { get; internal set; } = string.Empty;

    internal void BindAssetFile(string projectPath, string physicalPath, Guid id, string type)
    {
        BindAssetReference(projectPath, id);
        sourcePath = physicalPath ?? string.Empty;
        assetType = type ?? string.Empty;
    }
}
