namespace BEngine.Entities;

public readonly record struct ComponentType(Type Type, ComponentAccessMode AccessMode = ComponentAccessMode.ReadOnly)
{
    public static ComponentType ReadOnly<T>() => new(typeof(T));
    public static ComponentType ReadWrite<T>() => new(typeof(T), ComponentAccessMode.ReadWrite);
    public static ComponentType Exclude<T>() => new(typeof(T), ComponentAccessMode.Exclude);

    public override string ToString() => $"{AccessMode}:{Type.FullName}";
}
