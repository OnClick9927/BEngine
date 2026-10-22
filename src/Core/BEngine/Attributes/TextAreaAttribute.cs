namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TextAreaAttribute(int minLines = 3, int maxLines = 3) : PropertyAttribute
{
    public int minLines { get; } = minLines;
    public int maxLines { get; } = maxLines;
}
