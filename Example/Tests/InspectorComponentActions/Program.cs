using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.InspectorComponentActions;

internal static class Program
{
    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            using var workspace = new InspectorWorkspaceFixture();
            using var layers = new LayerTagConfiguration();
            InspectorPresentationTests.Run(workspace);
            GameObjectHeaderTests.Run(workspace);
            ComponentMenuTests.Run(workspace);
            ComponentExtensionMenuTests.Run(workspace);
            TransformClipboardTests.Run(workspace);
            ComponentClipboardComplexTests.Run(workspace);
            StructuralUndoTests.Run();
            RequireComponentUndoTests.Run();
            RequiredComponentRemovalTests.Run(workspace);
            ReadOnlyInspectorTests.Run(workspace);
            Console.WriteLine(
                "INSPECTOR_COMPONENT_ACTIONS_OK|gameobject-header,component-chrome,context-menu,component-onreset,context-menu-inheritance,context-menu-multiple,static-context-inheritance,component-menu-consistency,reset,copy,paste,complex-clipboard,missing-component-clipboard,edit-script,remove,local-transform,structural-undo,require-component-undo,required-removal-guard,readonly");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"INSPECTOR_COMPONENT_ACTIONS_FAILED|{exception}");
            return 1;
        }
        finally
        {
            GenericMenuCapture.Clear();
            Undo.ClearAll();
            GUIUtility.hotControl = 0;
            GUIUtility.keyboardControl = 0;
        }
    }
}
