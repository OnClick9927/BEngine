namespace BEngine.Serialization;
public interface IPrefabAuthoringBackend
{
    PrefabAsset SaveVariant(GameObject root, PrefabAsset basePrefab, string path, Guid? assetId);
}
