namespace BEngine.Documents;

public sealed class PrefabDocument : Document
{
    public string Format { get; set; } = "BEngine.Prefab";
    public int Version { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Prefab";
    public Guid Root { get; set; }
    public List<GameObjectDocument> GameObjects { get; set; } = [];
}
