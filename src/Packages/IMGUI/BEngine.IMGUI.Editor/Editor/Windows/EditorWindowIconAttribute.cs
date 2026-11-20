using System.Reflection;
using System.Linq.Expressions;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorWindowIconAttribute(string resourcePath) : Attribute
{
    public string resourcePath { get; } = string.IsNullOrWhiteSpace(resourcePath)
        ? throw new ArgumentException("Window icon resource path cannot be empty.", nameof(resourcePath))
        : resourcePath;
}
