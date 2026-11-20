namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class TooltipAttribute(string tooltip) : PropertyAttribute
{
    public string tooltip { get; } = tooltip;
}
