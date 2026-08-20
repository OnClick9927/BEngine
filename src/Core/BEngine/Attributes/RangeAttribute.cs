namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class RangeAttribute(float min, float max) : PropertyAttribute
{
    public float min { get; } = min;
    public float max { get; } = max;
}
