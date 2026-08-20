namespace BEngine.Documents;

public sealed class TransformDocument : Document
{
    public Guid Id { get; set; }
    public string Type { get; set; } = typeof(BEngine.Transform).FullName!;
    public Guid? PrefabAsset { get; set; }
    public Guid? PrefabSource { get; set; }
    public FixedVector2Document LocalPosition { get; set; } = new();
    public string LocalRotation { get; set; } = "0";
    public FixedVector2Document LocalScale { get; set; } = new(1, 1);
    public Dictionary<string, string> Fields { get; set; } = [];
}
