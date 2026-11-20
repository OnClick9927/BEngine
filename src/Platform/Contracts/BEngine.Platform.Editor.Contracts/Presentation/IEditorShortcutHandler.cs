namespace BEngine.Editor;
public interface IEditorShortcutHandler { bool Handle(KeyCode key, EventModifiers modifiers); }
public static class EditorShortcuts
{
    public static bool Handle(KeyCode key, EventModifiers modifiers)
    {
        {
            var __filteredSource243 = TypeCache.GetTypesDerivedFrom<IEditorShortcutHandler>();
            ArgumentNullException.ThrowIfNull(__filteredSource243, "source");
            foreach (var type in __filteredSource243)
            {
                if (!(!type.IsAbstract && !type.IsInterface))
                    continue;
                if (((IEditorShortcutHandler)Activator.CreateInstance(type)!).Handle(key, modifiers))
                    return true;
            }
        }
        return false;
    }
}
