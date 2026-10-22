namespace BEngine.Serialization;

internal sealed class PrefabAssetData
{
    public string Format { get; set; } = "BEngine.Prefab";
    public int Version { get; set; } = 3;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Prefab";
    public Guid Root { get; set; }
    public List<GameObjectData> GameObjects { get; set; } = [];
    public PrefabReferenceData? BasePrefab { get; set; }
    public List<PrefabReferenceData> Instances { get; set; } = [];
}
