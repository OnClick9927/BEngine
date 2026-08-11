namespace BEngine.Editor;

using BEngine.UIElements;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CustomEditorAttribute(Type inspectedType, bool editorForChildClasses = false) : Attribute
{
    public Type inspectedType { get; } = inspectedType;
    public bool editorForChildClasses { get; } = editorForChildClasses;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class CanEditMultipleObjectsAttribute : Attribute;

public abstract class Editor : ScriptableObject
{
    private SerializedObject? _serializedObject;
    public BObject target { get; internal set; } = null!;
    public BObject[] targets { get; internal set; } = [];
    public SerializedObject serializedObject => _serializedObject ??= new SerializedObject(targets);

    public virtual VisualElement? CreateInspectorGUI() => null;
    public virtual bool HasPreviewGUI() => false;
    public virtual void OnPreviewGUI(Rect previewArea) { }
    public virtual string GetInfoString() => string.Empty;
    public virtual bool RequiresConstantRepaint() => false;
    public virtual bool UseDefaultMargins() => true;
    public virtual void OnSceneGUI() { }

    public static Editor CreateEditor(BObject target) => CreateEditor(target, FindEditorType(target.GetType()));

    internal static bool HasCustomEditor(Type inspectedType) => FindCustomEditorType(inspectedType) is not null;

    public static Editor CreateEditor(BObject target, Type editorType)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(editorType);
        if (!typeof(Editor).IsAssignableFrom(editorType) || editorType.IsAbstract)
        {
            throw new ArgumentException($"{editorType.FullName} is not a concrete Editor type.", nameof(editorType));
        }

        var editor = (Editor)ScriptableObject.CreateInstance(editorType);
        editor.target = target;
        editor.targets = [target];
        return editor;
    }

    public static Editor CreateEditor(BObject[] targets, Type? editorType = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Length == 0) throw new ArgumentException("At least one target is required.", nameof(targets));
        editorType ??= FindEditorType(targets[0].GetType());
        var editor = CreateEditor(targets[0], editorType);
        editor.targets = [.. targets];
        editor._serializedObject = null;
        return editor;
    }

    public static void CreateCachedEditor(BObject target, Type? editorType, ref Editor? previousEditor)
    {
        if (previousEditor is not null && ReferenceEquals(previousEditor.target, target) &&
            (editorType is null || previousEditor.GetType() == editorType)) return;
        previousEditor = CreateEditor(target, editorType ?? FindEditorType(target.GetType()));
    }

    private static Type FindEditorType(Type inspectedType)
    {
        return FindCustomEditorType(inspectedType) ?? typeof(DefaultEditor);
    }

    private static Type? FindCustomEditorType(Type inspectedType)
    {
        return TypeCache.GetTypesDerivedFrom<Editor>()
                   .Where(type => !type.IsAbstract)
                   .SelectMany(type => type.GetCustomAttributes(typeof(CustomEditorAttribute), false)
                       .Cast<CustomEditorAttribute>().Select(attribute => (type, attribute)))
                   .Where(item => item.attribute.inspectedType == inspectedType ||
                                  item.attribute.editorForChildClasses &&
                                  item.attribute.inspectedType.IsAssignableFrom(inspectedType))
                   .Select(item => item.type)
                   .FirstOrDefault();
    }

    private sealed class DefaultEditor : Editor;
}
