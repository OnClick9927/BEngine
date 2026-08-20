namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ClampAttribute(double minimum, double maximum) : ExtendedPropertyAttribute
{
    public double minimum { get; } = Math.Min(minimum, maximum);
    public double maximum { get; } = Math.Max(minimum, maximum);
}
