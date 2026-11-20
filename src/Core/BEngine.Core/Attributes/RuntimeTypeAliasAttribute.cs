namespace BEngine;

/// <summary>Lets an extension preserve serialized identities after moving or renaming a type.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class RuntimeTypeAliasAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
