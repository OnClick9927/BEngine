namespace BEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class FormerlySerializedAsAttribute(string oldName) : Attribute
{
    public string oldName { get; } = string.IsNullOrWhiteSpace(oldName)
        ? throw new ArgumentException("A former serialized name is required.", nameof(oldName))
        : oldName;
}
