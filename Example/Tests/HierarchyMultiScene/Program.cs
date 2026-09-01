using BEngine.Editor;
using BEngine.Editor.Documents;

namespace BEngine.ExampleTests.HierarchyMultiScene;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Resources.RegisterResourceRoot(FindRepositoryRoot());
            EditorAppearance.Apply(new EditorPreferencesDocument());
            using var fixture = new SceneFixture();
            if (args.Contains("--play-asset-write-only", StringComparer.Ordinal))
            {
                // Match the full-suite state: earlier lifecycle tests have already imported fixture metadata.
                using (new EditorApplicationHarness(fixture)) { }
                EditorPlayModeAssetWriteTests.Run(fixture);
                Console.WriteLine("HIERARCHY_MULTI_SCENE_OK|play-project-write-guards,play-atlas-build-guard");
                return 0;
            }
            if (args.Contains("--window-locking-only", StringComparer.Ordinal))
            {
                WindowLockingTests.Run(fixture);
                Console.WriteLine("HIERARCHY_MULTI_SCENE_OK|locked-hierarchy-command-target," +
                                  "window-lock-context-roundtrip");
                return 0;
            }
            if (args.Contains("--hierarchy-ui-only", StringComparer.Ordinal))
            {
                HierarchyUnityStyleTests.RunObjectDrag(fixture);
                Console.WriteLine("HIERARCHY_MULTI_SCENE_OK|hierarchy-tree-selection," +
                                  "hierarchy-object-drag");
                return 0;
            }
            if (args.Contains("--scene-tools-only", StringComparer.Ordinal))
            {
                SceneGizmoDispatchTests.Run();
                PackageGizmoDrawerTests.Run();
                SceneGizmoToolbarTests.Run(fixture);
                SceneViewportResizeTests.Run(fixture);
                SceneHandleVisualTests.Run(fixture);
                ScenePickingVisibilityTests.Run(fixture);
                Console.WriteLine("HIERARCHY_MULTI_SCENE_OK|camera2d-selected-gizmo," +
                                  "scene-qwer-tools,scene-view-pan,scene-handle-focus," +
                                  "scene-picking-handle-isolation");
                return 0;
            }
            if (args.Contains("--multi-window-only", StringComparer.Ordinal))
            {
                MultiWindowLifecycleTests.Run(fixture);
                Console.WriteLine("HIERARCHY_MULTI_SCENE_OK|multi-window-instances," +
                                  "multi-inspector-play-isolation,close-layout-restore");
                return 0;
            }
            EditorSceneContractTests.Run();
            SceneGizmoDispatchTests.Run();
            PackageGizmoDrawerTests.Run();
            SceneGizmoToolbarTests.Run(fixture);
            SceneViewportResizeTests.Run(fixture);
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
            WindowLockingTests.Run(fixture);
            MultiWindowLifecycleTests.Run(fixture);
            Console.WriteLine(
                "HIERARCHY_MULTI_SCENE_OK|scene-ownership,single,additive,unloaded,active,close,scene-gizmo-selection-dispatch,scene-gizmo-camera-aspect-stability,scene-gizmo-default-white-thick-lines,scene-gizmo-selected-only-builtins,scene-gizmo-external-drawer-inheritance,scene-gizmo-type-visibility,scene-gizmo-package-selected-only,scene-gizmo-package-drawers,scene-gizmo-toolbar,scene-gizmo-narrow-menu,scene-gizmo-width-stability,scene-resize-fixed-world-scale,scene-resize-reveal-only,scene-handle-distinct-wer,scene-handle-viewport-clip,scene-rotate-ring-drag,scene-rotate-ring-angle-wrap,scene-rotate-offset-pivot-center,scene-scale-local-axes,scene-scale-uniform-center,scene-handle-focus-loss-cancel,scene-handle-tool-switch-cancel,scene-pick-render-order-cycle,scene-pick-hierarchy-reveal,scene-pick-package-tilemap,scene-pick-empty-clear,scene-pick-visibility,scene-pick-disable,scene-hierarchy-fixed-eye-lock,scene-handle-pick-capture,scene-visibility-play-mirror,play-mirror,play-restore,play-component-field-isolation,play-value-clone-isolation,play-asset-isolation,play-save-entry-guards,play-project-write-guards,play-atlas-build-guard,play-undo-isolation,play-transition-guard,play-teardown-save-guard,play-game-focus,play-runtime-global-state,play-single-scene-switch,play-runtime-scene-lifecycle,locked-inspector-mapping,unity-toolbar,add-dropdown,search,scene-actions,tree-icons,indent,foldout,row-height,selection,hover,narrow-text,drag,rename,project-ping,frame-selected,dont-destroy-on-load,shared-gameobject-menu,2d-create,2d-transform,gameobject-hierarchy,main-menu-contract,main-menu-shortcuts,main-menu-validation,main-menu-actions,main-menu-fault-isolation,locked-hierarchy-command-target,window-lock-context-roundtrip");
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

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null; directory = directory.Parent)
        {
            var core = Path.Combine(directory.FullName, "src", "Core");
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln"))) return core;
        }
        throw new DirectoryNotFoundException("Could not locate the Core resource root.");
    }
}
