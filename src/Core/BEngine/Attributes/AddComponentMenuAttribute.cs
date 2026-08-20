namespace BEngine;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AddComponentMenuAttribute(string componentMenu, int componentOrder = 0) : Attribute
{
    public string componentMenu { get; } = componentMenu;
    public int componentOrder { get; } = componentOrder;
}
