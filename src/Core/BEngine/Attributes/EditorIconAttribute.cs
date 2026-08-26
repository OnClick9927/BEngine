namespace BEngine;

/// <summary>
/// Associates an engine object type with an icon stored below a package's Editor directory.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class EditorIconAttribute(string resourcePath) : Attribute
{
    public string resourcePath { get; } = string.IsNullOrWhiteSpace(resourcePath)
        ? throw new ArgumentException("An editor icon resource path is required.", nameof(resourcePath))
        : resourcePath.Replace('\\', '/').TrimStart('/');
}
