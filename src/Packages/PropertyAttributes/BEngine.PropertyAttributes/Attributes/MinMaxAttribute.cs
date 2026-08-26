namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MinMaxAttribute(float minimum, float maximum) : ExtendedPropertyAttribute
{
    public float minimum { get; } = Math.Min(minimum, maximum);
    public float maximum { get; } = Math.Max(minimum, maximum);
}
