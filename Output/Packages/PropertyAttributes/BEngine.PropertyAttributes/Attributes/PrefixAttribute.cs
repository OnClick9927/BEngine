namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class PrefixAttribute(string text) : ExtendedPropertyAttribute
{
    public string text { get; } = text;
}
