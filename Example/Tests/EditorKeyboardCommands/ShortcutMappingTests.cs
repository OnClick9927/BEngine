using BEngine.Editor;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class ShortcutMappingTests
{
    internal static void Run()
    {
        VerifyCommands();
        VerifyWindowShortcuts();
        VerifySceneTools();
        VerifyModifierIsolation();
    }

    private static void VerifyCommands()
    {
        Require(KeyCode.N, EventModifiers.Control, EditorShortcut.NewScene);
        Require(KeyCode.O, EventModifiers.Command, EditorShortcut.OpenScene);
        Require(KeyCode.S, EventModifiers.Control, EditorShortcut.SaveScene);
        Require(KeyCode.Z, EventModifiers.Control, EditorShortcut.Undo);
        Require(KeyCode.Z, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.Redo);
        Require(KeyCode.Y, EventModifiers.Control, EditorShortcut.Redo);
        Require(KeyCode.C, EventModifiers.Control, EditorShortcut.Copy);
        Require(KeyCode.V, EventModifiers.Command, EditorShortcut.Paste);
        Require(KeyCode.D, EventModifiers.Control, EditorShortcut.Duplicate);
        Require(KeyCode.A, EventModifiers.Control, EditorShortcut.SelectAll);
        Require(KeyCode.R, EventModifiers.Control, EditorShortcut.RefreshAssets);
        Require(KeyCode.N, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.CreateEmptyGameObject);
        Require(KeyCode.P, EventModifiers.Control, EditorShortcut.Play);
        Require(KeyCode.P, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.Pause);
        Require(KeyCode.P, EventModifiers.Control | EventModifiers.Alt, EditorShortcut.Step);
        Require(KeyCode.F2, EventModifiers.FunctionKey, EditorShortcut.Rename);
        Require(KeyCode.Delete, EventModifiers.None, EditorShortcut.Delete);
    }

    private static void VerifyWindowShortcuts()
    {
        Require(KeyCode.Alpha1, EventModifiers.Control, EditorShortcut.SceneWindow);
        Require(KeyCode.Alpha2, EventModifiers.Control, EditorShortcut.GameWindow);
        Require(KeyCode.Alpha3, EventModifiers.Control, EditorShortcut.InspectorWindow);
        Require(KeyCode.Alpha4, EventModifiers.Control, EditorShortcut.HierarchyWindow);
        Require(KeyCode.Alpha5, EventModifiers.Control, EditorShortcut.ProjectWindow);
        Require(KeyCode.Alpha7, EventModifiers.Control, EditorShortcut.ProfilerWindow);
        Require(KeyCode.C, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.ConsoleWindow);
    }

    private static void VerifySceneTools()
    {
        Require(KeyCode.Q, EventModifiers.None, EditorShortcut.ViewTool);
        Require(KeyCode.W, EventModifiers.None, EditorShortcut.MoveTool);
        Require(KeyCode.E, EventModifiers.None, EditorShortcut.RotateTool);
        Require(KeyCode.R, EventModifiers.None, EditorShortcut.ScaleTool);
        Require(KeyCode.F, EventModifiers.None, EditorShortcut.FrameSelected);
    }

    private static void VerifyModifierIsolation()
    {
        Require(KeyCode.N, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.CreateEmptyGameObject);
        Require(KeyCode.C, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.ConsoleWindow);
        Require(KeyCode.R, EventModifiers.Control | EventModifiers.Alt, EditorShortcut.None);
        Require(KeyCode.D, EventModifiers.Control | EventModifiers.Shift, EditorShortcut.None);
        Require(KeyCode.Q, EventModifiers.Shift, EditorShortcut.None);
        Require(KeyCode.F, EventModifiers.Control, EditorShortcut.None);
    }

    private static void Require(KeyCode keyCode, EventModifiers modifiers, EditorShortcut expected)
    {
        var actual = EditorShortcutMap.Resolve(keyCode, modifiers);
        TestAssert.Require(actual == expected,
            $"Shortcut {modifiers}+{keyCode} resolved to {actual} instead of {expected}.");
    }
}
