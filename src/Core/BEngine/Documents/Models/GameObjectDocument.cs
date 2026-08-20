namespace BEngine.Documents;

public sealed class GameObjectDocument : Document
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "GameObject";
    public bool Active { get; set; } = true;
    public string Tag { get; set; } = "Untagged";
    public ulong Layer { get; set; } = SortingLayer.Default;
    public bool IsStatic { get; set; }
    public Guid? Parent { get; set; }
    public Guid? PrefabAsset { get; set; }
    public Guid? PrefabSource { get; set; }
    public TransformDocument Transform { get; set; } = new();
    public List<ComponentDocument> Components { get; set; } = [];
}
