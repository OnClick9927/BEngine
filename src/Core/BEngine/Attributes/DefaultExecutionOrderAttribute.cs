namespace BEngine;

[AttributeUsage(AttributeTargets.Class)]
public sealed class DefaultExecutionOrderAttribute(int order) : Attribute
{
    public int order { get; } = order;
}
