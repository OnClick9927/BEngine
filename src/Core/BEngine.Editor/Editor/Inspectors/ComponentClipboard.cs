namespace BEngine.Editor;

internal static class ComponentClipboard
{
    private static ComponentValueSnapshot? _snapshot;

    internal static bool hasValue => _snapshot is not null;

    internal static void Copy(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        _snapshot = ComponentValueSnapshot.Capture(component);
    }

    internal static bool CanPaste(Component component) => component is not null &&
        _snapshot?.CanApplyTo(component) is true;

    internal static void Clear()
    {
        _snapshot = null;
    }

    internal static bool Paste(Component component)
    {
        if (!CanPaste(component) || _snapshot is null) return false;
        Undo.RecordObject(component, $"Paste {component.GetType().Name} Values");
        _snapshot.ApplyTo(component);
        Validate(component);
        EditorUtility.SetDirty(component);
        return true;
    }

    internal static bool Reset(Component component) => Reset(component, recordUndo: true);

    internal static bool Reset(Component component, bool recordUndo)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (component is MissingComponent) return false;
        var defaultsOwner = new GameObject("Component Defaults");
        var defaults = component.GetType() == typeof(Transform)
            ? defaultsOwner.transform
            : defaultsOwner.AddComponent(component.GetType());
        var defaultsSnapshot = ComponentValueSnapshot.Capture(defaults);
        if (recordUndo) Undo.RecordObject(component, $"Reset {component.GetType().Name}");
        defaultsSnapshot.ApplyTo(component);
        if (component is MonoBehaviour behaviour)
            EditorFeatureGuard.Invoke(behaviour, nameof(MonoBehaviour.Reset), behaviour.Reset);
        Validate(component);
        EditorUtility.SetDirty(component);
        return true;
    }

    private static void Validate(Component component)
    {
        if (component is MonoBehaviour behaviour)
            EditorFeatureGuard.Invoke(behaviour, nameof(MonoBehaviour.OnValidate), behaviour.OnValidate);
    }
}
