namespace BEngine.Documents;

public sealed class SceneDocument : Document
{
    public string Format { get; set; } = "BEngine.Scene";
    public int Version { get; set; } = 1;
    public Guid Id { get; set; }
    public string Name { get; set; } = "Untitled";
    public List<GameObjectDocument> GameObjects { get; set; } = [];
}
