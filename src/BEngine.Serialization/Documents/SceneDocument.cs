namespace BEngine.Serialization.Documents;

public sealed class SceneDocument
{
    public string Format { get; set; } = "BEngine.Scene";
    public int Version { get; set; } = 1;
    public Guid Id { get; set; }
    public string Name { get; set; } = "Untitled";
    public List<GameObjectDocument> GameObjects { get; set; } = [];
}

public sealed class GameObjectDocument
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "GameObject";
    public bool Active { get; set; } = true;
    public Guid? Parent { get; set; }
    public TransformDocument Transform { get; set; } = new();
    public List<ComponentDocument> Components { get; set; } = [];
}

public sealed class TransformDocument
{
    public Guid Id { get; set; }
    public string Type { get; set; } = typeof(BEngine.Transform).FullName!;
    public FixedVector3Document LocalPosition { get; set; } = new();
    public FixedVector3Document LocalEulerAngles { get; set; } = new();
    public FixedVector3Document LocalScale { get; set; } = new(1, 1, 1);
    public Dictionary<string, string> Fields { get; set; } = [];
}

public sealed class FixedVector3Document
{
    public string X { get; set; } = "0";
    public string Y { get; set; } = "0";
    public string Z { get; set; } = "0";

    public FixedVector3Document() { }

    public FixedVector3Document(BEngine.Vector3 value)
    {
        X = value.x.ToString();
        Y = value.y.ToString();
        Z = value.z.ToString();
    }

    public FixedVector3Document(BEngine.Fix64 x, BEngine.Fix64 y, BEngine.Fix64 z)
        : this(new BEngine.Vector3(x, y, z))
    {
    }

    public BEngine.Vector3 ToVector3() => new(
        BEngine.Fix64.Parse(X), BEngine.Fix64.Parse(Y), BEngine.Fix64.Parse(Z));
}

public sealed class ComponentDocument
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Fields { get; set; } = [];
}
