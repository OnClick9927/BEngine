using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class Program
{
    private static int Main()
    {
        try
        {
            EditorAppearance.Apply(new EditorPreferencesDocument());
            using var fixture = new SceneFixture();
            EditorSceneContractTests.Run();
            RuntimeSceneLoadingTests.Run(fixture);
            EditorSceneLifecycleTests.Run(fixture);
            HierarchyUnityStyleTests.Run(fixture);
            HierarchyInteractionTests.Run(fixture);
            GameObjectMenuTests.Run(fixture);
            MainMenuIntegrationTests.Run(fixture);
            Console.WriteLine(
                "HIERARCHY_MULTI_SCENE_OK|scene-ownership,single,additive,unloaded,active,close,unity-toolbar,add-dropdown,search,scene-actions,tree-icons,indent,foldout,row-height,selection,hover,narrow-text,drag,rename,project-ping,frame-selected,dont-destroy-on-load,shared-gameobject-menu,2d-create,2d-transform,gameobject-hierarchy,main-menu-contract,main-menu-shortcuts,main-menu-validation,main-menu-actions,main-menu-fault-isolation");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"HIERARCHY_MULTI_SCENE_FAILED|{exception}");
            return 1;
        }
        finally
        {
            GenericMenuCapture.Clear();
        }
    }
}
