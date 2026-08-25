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
            SceneGizmoDispatchTests.Run();
            PackageGizmoDrawerTests.Run();
            SceneGizmoToolbarTests.Run(fixture);
            SceneHandleVisualTests.Run(fixture);
            ScenePickingVisibilityTests.Run(fixture);
            EditorPlayModeIsolationTests.Run(fixture);
            EditorPlayModeValueCloneTests.Run(fixture);
            EditorPlayModeLifecycleTests.Run(fixture);
            EditorPlayModeAssetWriteTests.Run(fixture);
            RuntimeSceneLoadingTests.Run(fixture);
            EditorSceneLifecycleTests.Run(fixture);
            HierarchyUnityStyleTests.Run(fixture);
            HierarchyInteractionTests.Run(fixture);
            GameObjectMenuTests.Run(fixture);
            MainMenuIntegrationTests.Run(fixture);
            Console.WriteLine(
                "HIERARCHY_MULTI_SCENE_OK|scene-ownership,single,additive,unloaded,active,close,scene-gizmo-selection-dispatch,scene-gizmo-camera-aspect-stability,scene-gizmo-selected-only-builtins,scene-gizmo-external-drawer-inheritance,scene-gizmo-type-visibility,scene-gizmo-package-selected-only,scene-gizmo-package-drawers,scene-gizmo-toolbar,scene-gizmo-narrow-menu,scene-gizmo-width-stability,scene-handle-distinct-wer,scene-handle-viewport-clip,scene-rotate-ring-drag,scene-rotate-ring-angle-wrap,scene-rotate-offset-pivot-center,scene-scale-local-axes,scene-scale-uniform-center,scene-handle-focus-loss-cancel,scene-handle-tool-switch-cancel,scene-pick-render-order-cycle,scene-pick-package-tilemap,scene-pick-empty-clear,scene-pick-visibility,scene-pick-disable,scene-hierarchy-eye-lock,scene-handle-pick-capture,scene-visibility-play-mirror,play-mirror,play-restore,play-component-field-isolation,play-value-clone-isolation,play-asset-isolation,play-save-entry-guards,play-project-write-guards,play-undo-isolation,play-transition-guard,play-teardown-save-guard,play-game-focus,play-runtime-global-state,play-single-scene-switch,play-runtime-scene-lifecycle,locked-inspector-mapping,unity-toolbar,add-dropdown,search,scene-actions,tree-icons,indent,foldout,row-height,selection,hover,narrow-text,drag,rename,project-ping,frame-selected,dont-destroy-on-load,shared-gameobject-menu,2d-create,2d-transform,gameobject-hierarchy,main-menu-contract,main-menu-shortcuts,main-menu-validation,main-menu-actions,main-menu-fault-isolation");
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
