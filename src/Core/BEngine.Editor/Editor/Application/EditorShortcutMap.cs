namespace BEngine.Editor;

internal static class EditorShortcutMap
{
    private const EventModifiers ShortcutModifiers =
        EventModifiers.Shift | EventModifiers.Control | EventModifiers.Alt | EventModifiers.Command;
    private const EventModifiers ActionModifiers = EventModifiers.Control | EventModifiers.Command;

    internal static EditorShortcut Resolve(KeyCode keyCode, EventModifiers modifiers)
    {
        var normalized = modifiers & ShortcutModifiers;
        var hasActionModifier = (normalized & ActionModifiers) != 0;
        var extraModifiers = normalized & ~ActionModifiers;

        if (!hasActionModifier)
        {
            if (normalized != EventModifiers.None) return EditorShortcut.None;
            return keyCode switch
            {
                KeyCode.Q => EditorShortcut.ViewTool,
                KeyCode.W => EditorShortcut.MoveTool,
                KeyCode.E => EditorShortcut.RotateTool,
                KeyCode.R => EditorShortcut.ScaleTool,
                KeyCode.F => EditorShortcut.FrameSelected,
                KeyCode.F2 => EditorShortcut.Rename,
                KeyCode.Delete => EditorShortcut.Delete,
                _ => EditorShortcut.None
            };
        }

        if (extraModifiers == EventModifiers.Shift)
        {
            return keyCode switch
            {
                KeyCode.Z => EditorShortcut.Redo,
                KeyCode.N => EditorShortcut.CreateEmptyGameObject,
                KeyCode.C => EditorShortcut.ConsoleWindow,
                KeyCode.P => EditorShortcut.Pause,
                _ => EditorShortcut.None
            };
        }

        if (extraModifiers == EventModifiers.Alt)
            return keyCode == KeyCode.P ? EditorShortcut.Step : EditorShortcut.None;
        if (extraModifiers != EventModifiers.None) return EditorShortcut.None;

        return keyCode switch
        {
            KeyCode.N => EditorShortcut.NewScene,
            KeyCode.O => EditorShortcut.OpenScene,
            KeyCode.S => EditorShortcut.SaveScene,
            KeyCode.Z => EditorShortcut.Undo,
            KeyCode.Y => EditorShortcut.Redo,
            KeyCode.C => EditorShortcut.Copy,
            KeyCode.V => EditorShortcut.Paste,
            KeyCode.D => EditorShortcut.Duplicate,
            KeyCode.A => EditorShortcut.SelectAll,
            KeyCode.P => EditorShortcut.Play,
            KeyCode.R => EditorShortcut.RefreshAssets,
            KeyCode.Alpha1 => EditorShortcut.SceneWindow,
            KeyCode.Alpha2 => EditorShortcut.GameWindow,
            KeyCode.Alpha3 => EditorShortcut.InspectorWindow,
            KeyCode.Alpha4 => EditorShortcut.HierarchyWindow,
            KeyCode.Alpha5 => EditorShortcut.ProjectWindow,
            KeyCode.Alpha7 => EditorShortcut.ProfilerWindow,
            _ => EditorShortcut.None
        };
    }

    internal static MainMenuCommand? MainMenuCommandFor(EditorShortcut shortcut) => shortcut switch
    {
        EditorShortcut.FrameSelected => MainMenuCommand.FrameSelected,
        EditorShortcut.NewScene => MainMenuCommand.NewScene,
        EditorShortcut.OpenScene => MainMenuCommand.OpenScene,
        EditorShortcut.SaveScene => MainMenuCommand.SaveScene,
        EditorShortcut.Undo => MainMenuCommand.Undo,
        EditorShortcut.Redo => MainMenuCommand.Redo,
        EditorShortcut.Copy => MainMenuCommand.Copy,
        EditorShortcut.Paste => MainMenuCommand.Paste,
        EditorShortcut.Duplicate => MainMenuCommand.Duplicate,
        EditorShortcut.SelectAll => MainMenuCommand.SelectAll,
        EditorShortcut.Play => MainMenuCommand.Play,
        EditorShortcut.Pause => MainMenuCommand.Pause,
        EditorShortcut.Step => MainMenuCommand.Step,
        EditorShortcut.Rename => MainMenuCommand.Rename,
        EditorShortcut.Delete => MainMenuCommand.Delete,
        _ => null
    };
}
