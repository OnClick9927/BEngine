namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class AssetPathAttribute(string? extension = null) : ExtendedPropertyAttribute
{
    public string? extension { get; } = extension;
}
