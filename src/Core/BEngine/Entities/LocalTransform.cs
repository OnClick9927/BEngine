namespace BEngine.Entities;

public struct LocalTransform : IComponentData
{
    public Vector2 Position;
    public Fix64 Rotation;
    public Vector2 Scale;

    public LocalTransform(Vector2 position, Fix64 rotation, Vector2 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public static LocalTransform Identity => new(Vector2.zero, Fix64.Zero, Vector2.one);
}
