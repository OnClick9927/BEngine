namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class ColorUsageAttribute(bool showAlpha = true, bool hdr = false) : PropertyAttribute
{
    public bool showAlpha { get; } = showAlpha;
    public bool hdr { get; } = hdr;
}
