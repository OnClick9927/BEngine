namespace BEngine.Editor;
public sealed class ProfilerShortcut : IEditorShortcutHandler
{
    public bool Handle(KeyCode key, EventModifiers modifiers)
    {
        var action = EventModifiers.Control | EventModifiers.Command;
        if (key != KeyCode.Alpha7 || (modifiers & action) == 0 || (modifiers & (EventModifiers.Shift | EventModifiers.Alt)) != 0) return false;
        EditorTool.Open("profiler"); return true;
    }
}
