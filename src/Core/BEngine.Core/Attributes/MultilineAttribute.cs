namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MultilineAttribute(int lines = 3) : PropertyAttribute
{
    public int lines { get; } = lines;
}
