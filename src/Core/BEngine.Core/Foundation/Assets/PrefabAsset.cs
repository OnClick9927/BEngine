using BEngine.Serialization;

namespace BEngine;

/// <summary>A reusable, YAML-backed GameObject hierarchy.</summary>
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
    public bool isVariant => _data.BasePrefab is not null;
    public string basePrefab => _data.BasePrefab?.Asset ?? string.Empty;

    public int objectCount
    {
        get { return PrefabResolver.Resolve(this).GameObjects.Count; }
    }
    public int componentCount
    {
        get
        {
            return Scan1003(PrefabResolver.Resolve(this).GameObjects);
            [global::BEngine.SkipLoopGuard]
            int Scan1003(global::System.Collections.Generic.IEnumerable<global::BEngine.Serialization.GameObjectData> scanSource1003)
            {
                ArgumentNullException.ThrowIfNull(scanSource1003, "source");
                int total = 0;
                foreach (var scanItem1003 in scanSource1003)
                    total = checked(total + Match1003(scanItem1003));
                return total;
                int Match1003(global::BEngine.Serialization.GameObjectData item) => item.Components.Count + 1;
            }
        }
    }

    public GameObject Instantiate(Scene scene, Transform? parent = null)
    {
        ThrowIfDestroyed();
        return PrefabAssetOperations.Instantiate(this, scene, parent);
    }
}
