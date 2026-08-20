using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.GameObjectLayerTag;

internal static class Program
{
    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            ProjectSettingsCompatibilityTests.Run();
            using var configuration = LayerTagConfiguration.ApplyTestConfiguration();
            RuntimeLayerTagTests.Run();
            RenderOrderingTests.Run();
            HierarchyStateTests.Run();
            SceneSerializationTests.Run();
            InspectorSelectorTests.Run();
            Console.WriteLine(
                "GAMEOBJECT_LAYER_TAG_OK|defaults,power-of-two-layers,ui-boundary,sprite-particle-boundary,render-order,batching,tag-compare,hierarchy-active,scene-yaml,inspector-popups");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"GAMEOBJECT_LAYER_TAG_FAILED|{exception}");
            return 1;
        }
        finally
        {
            GenericMenuCapture.Clear();
        }
    }
}
