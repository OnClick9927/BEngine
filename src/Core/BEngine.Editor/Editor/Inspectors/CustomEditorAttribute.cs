using System.Reflection;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CustomEditorAttribute(Type inspectedType, bool editorForChildClasses = false) : Attribute
{
    public Type inspectedType { get; } = inspectedType;
    public bool editorForChildClasses { get; } = editorForChildClasses;
}
