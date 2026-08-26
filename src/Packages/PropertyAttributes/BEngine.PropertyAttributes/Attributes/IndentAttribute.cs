namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class IndentAttribute(int level = 1) : ExtendedPropertyAttribute
{
    public int level { get; } = Math.Max(0, level);
}
