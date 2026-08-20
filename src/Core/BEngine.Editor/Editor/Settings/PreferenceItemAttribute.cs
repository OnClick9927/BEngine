using System.Reflection;
using System.Runtime.CompilerServices;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PreferenceItemAttribute(string name) : Attribute
{
    public string name { get; } = string.IsNullOrWhiteSpace(name)
        ? throw new ArgumentException("Preference item name cannot be empty.", nameof(name))
        : name;
}
