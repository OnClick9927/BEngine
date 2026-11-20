namespace BEngine;

/// <summary>Optional authoring document codec. Object capture/restore and cloning stay in Core.</summary>
public interface ISceneDocumentCodec
{
    Scene Load(string path, IServiceProvider? services);
    Scene Deserialize(string text, IServiceProvider? services);
    string Serialize(Scene scene);
    void Save(Scene scene, string path);
}

public interface IPrefabDocumentCodec
{
    PrefabAsset Load(string path);
    PrefabAsset Deserialize(string text, string sourcePath);
    string Serialize(PrefabAsset prefab);
    string Serialize(GameObject root, Guid? assetId);
    void Save(PrefabAsset prefab, string path);
    PrefabAsset Save(GameObject root, string path, Guid? assetId);
    Guid? ReadAssetId(string path);
}

/// <summary>Source-document persistence for native managed objects. Object graph encoding stays in Core.</summary>
public interface IManagedAssetDocumentCodec
{
    BObject Load(string path);
    void Save(BObject asset, string path);
}
