namespace BEngine.Editor;

/// <summary>Opts an EditorWindow type into the Add new tab menu.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorWindowTabAttribute(string menuPath = "") : Attribute
{
    public string menuPath { get; } = menuPath ?? string.Empty;
}
