namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class SpaceAttribute(float height = 8f) : PropertyAttribute
{
    public float height { get; } = height;
}
