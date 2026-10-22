namespace BEngine.Editor;
public interface IEditorShortcutHandler { bool Handle(KeyCode key, EventModifiers modifiers); }
public static class EditorShortcuts
{
    public static bool Handle(KeyCode key, EventModifiers modifiers)
    {
        foreach (var type in TypeCache.GetTypesDerivedFrom<IEditorShortcutHandler>().Where(t => !t.IsAbstract && !t.IsInterface))
            if (((IEditorShortcutHandler)Activator.CreateInstance(type)!).Handle(key, modifiers)) return true;
        return false;
    }
}
