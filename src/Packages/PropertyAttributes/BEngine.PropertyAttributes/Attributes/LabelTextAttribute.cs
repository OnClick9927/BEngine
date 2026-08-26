namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class LabelTextAttribute(string label) : ExtendedPropertyAttribute
{
    public string label { get; } = label;
}
