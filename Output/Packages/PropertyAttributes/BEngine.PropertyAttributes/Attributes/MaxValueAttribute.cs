namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class MaxValueAttribute(double maximum) : ExtendedPropertyAttribute
{
    public double maximum { get; } = maximum;
}
