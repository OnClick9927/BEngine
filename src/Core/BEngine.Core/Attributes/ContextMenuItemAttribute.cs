namespace BEngine;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public sealed class ContextMenuItemAttribute(string name, string function) : PropertyAttribute
{
    public string name { get; } = name;
    public string function { get; } = function;
}
