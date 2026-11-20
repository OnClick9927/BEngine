namespace BEngine;

public readonly record struct RuntimeMemberAccessor(
    Type ValueType,
    Func<object, object?> Getter,
    Action<object, object?>? Setter)
{
    public bool CanWrite => Setter is not null;
}
