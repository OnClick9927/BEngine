namespace BEngine.PropertyAttributes;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TitleAttribute(string title, string? subtitle = null) : ExtendedPropertyAttribute
{
    public string title { get; } = title;
    public string? subtitle { get; } = subtitle;
}
