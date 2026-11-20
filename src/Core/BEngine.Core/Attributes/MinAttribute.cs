namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MinAttribute(float min) : PropertyAttribute
{
    public float min { get; } = min;
}
