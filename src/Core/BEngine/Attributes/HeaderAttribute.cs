namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class HeaderAttribute(string header) : PropertyAttribute
{
    public string header { get; } = header;
}
