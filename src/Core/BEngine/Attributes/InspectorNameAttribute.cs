namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class InspectorNameAttribute(string displayName) : PropertyAttribute
{
    public string displayName { get; } = displayName;
}
