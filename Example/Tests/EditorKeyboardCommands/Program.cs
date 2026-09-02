using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.EditorKeyboardCommands;

internal static class Program
{
    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            ShortcutMappingTests.Run();
            UndoBehaviorTests.Run();
            HierarchyKeyboardTests.Run();
            ProjectKeyboardTests.Run();
            TextInputShortcutIsolationTests.Run();
            Console.WriteLine(
                "EDITOR_KEYBOARD_COMMANDS_OK|unity-shortcut-map,undo,redo,delete,f2,copy,duplicate,paste,hierarchy,project,text-focus-isolation");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"EDITOR_KEYBOARD_COMMANDS_FAILED|{exception}");
            return 1;
        }
        finally
        {
            Undo.ClearAll();
            GUIUtility.keyboardControl = 0;
        }
    }
}
