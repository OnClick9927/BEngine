namespace BEngine;

/// <summary>Declares a persisted assembly name now owned by this assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RuntimeAssemblyAliasAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
