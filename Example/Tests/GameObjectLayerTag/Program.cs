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
            CameraRenderingTests.Run();
            SceneHandleTests.Run();
            HierarchyStateTests.Run();
            SceneSerializationTests.Run();
            InspectorSelectorTests.Run();
            Console.WriteLine(
                "GAMEOBJECT_LAYER_TAG_OK|defaults,natural-index-layers,ui-metadata,sprite-particle-boundary,render-order,batching,camera-stack,camera-mask,camera-culling,camera-viewport,legacy-camera-depth,scene-handle-center,tag-compare,hierarchy-active,scene-yaml,inspector-popups,tag-layer-settings-links");
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
