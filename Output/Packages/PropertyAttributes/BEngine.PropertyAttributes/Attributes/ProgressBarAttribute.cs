namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ProgressBarAttribute(float minimum = 0, float maximum = 100, string? title = null)
    : ExtendedPropertyAttribute
{
    public float minimum { get; } = minimum;
    public float maximum { get; } = maximum;
    public string? title { get; } = title;
    public bool editable { get; init; } = true;
}
