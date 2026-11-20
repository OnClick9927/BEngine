using System;

namespace BEngine.App.Value;

/// <summary>Marks an instance member for explicit injection by <see cref="IValueService.Inject"/>.</summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class InjectAttribute : Attribute
{
    public InjectAttribute(string? name = null) => Name = string.IsNullOrEmpty(name) ? null : name;
    public string? Name { get; }
}
