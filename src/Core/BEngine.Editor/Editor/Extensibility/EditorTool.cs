using System.Reflection;

namespace BEngine.Editor;

[AttributeUsage(AttributeTargets.Class)]
public sealed class EditorToolAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

public interface IEditorTool
{
    void Open(EditorWindow? target);
}

/// <summary>Discovers optional tools in currently loaded assemblies.</summary>
public static class EditorTool
{
    public static bool IsAvailable(string id) => Find(id) is not null;

    public static bool Open(string id, EditorWindow? target = null)
    {
        var type = Find(id);
        if (type is null) return false;
        ((IEditorTool)Activator.CreateInstance(type)!).Open(target);
        return true;
    }

    private static Type? Find(string id) => TypeCache.GetTypesDerivedFrom<IEditorTool>()
        .FirstOrDefault(type => !type.IsAbstract && !type.ContainsGenericParameters &&
            type.GetCustomAttribute<EditorToolAttribute>()?.Id == id);
}

