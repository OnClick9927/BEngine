namespace BEngine.Entities;

public readonly record struct Entity(int Index, int Version)
{
    public static Entity Null => default;
    public bool IsNull => Index == 0;

    public override string ToString() => IsNull ? "Entity.Null" : $"Entity({Index}:{Version})";
}
