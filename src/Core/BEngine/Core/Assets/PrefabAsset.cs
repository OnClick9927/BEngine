using BEngine.Serialization;

namespace BEngine;

/// <summary>A reusable, YAML-backed GameObject hierarchy.</summary>
[EditorIcon("Icons/Assets/AssetPrefab.png")]
public sealed class PrefabAsset : BAsset
{
    private readonly PrefabAssetData _data;

    internal PrefabAssetData Data
    {
        get { return _data; }
    }
    internal PrefabAssetData DataUnchecked => _data;

    internal PrefabAsset(PrefabAssetData document, string sourcePath)
    {
        _data = document ?? throw new ArgumentNullException(nameof(document));
        assetPath = sourcePath ?? string.Empty;
        name = document.Name;
    }

    public Guid assetId
    {
        get { return _data.Id; }
    }
    public int objectCount
    {
        get { return _data.GameObjects.Count; }
    }
    public int componentCount
    {
        get
        {
            return _data.GameObjects.Sum(item => item.Components.Count + 1);
        }
    }

    public GameObject Instantiate(Scene scene, Transform? parent = null)
    {
        return PrefabAssetOperations.Instantiate(this, scene, parent);
    }
}
