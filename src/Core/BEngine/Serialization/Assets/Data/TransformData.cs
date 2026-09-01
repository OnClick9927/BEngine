namespace BEngine.Serialization;

internal sealed class TransformData
{
    public Guid Id { get; set; }
    public string Type { get; set; } = typeof(BEngine.Transform).FullName!;
    public Guid? PrefabAsset { get; set; }
    public Guid? PrefabSource { get; set; }
    public FixedVector2Data LocalPosition { get; set; } = new();
    public string LocalRotation { get; set; } = "0";
    public FixedVector2Data LocalScale { get; set; } = new(1, 1);
    public Dictionary<string, string> Fields { get; set; } = [];
}
