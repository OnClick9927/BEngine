namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HorizontalLineAttribute(float thickness = 1, string color = "#55585e") : ExtendedPropertyAttribute
{
    public float thickness { get; } = Math.Max(1, thickness);
    public string color { get; } = color;
}
