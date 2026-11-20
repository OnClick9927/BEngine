namespace BEngine;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ContextMenuAttribute(string itemName) : Attribute
{
    public string itemName { get; } = itemName;
}
