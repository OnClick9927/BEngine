using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class Rendering2DEditorModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        Tools.HandlePositionProvider = SceneHandleUtility.GetHandlePosition;
        TypeCache.RegisterWarmup("SceneGizmos", GizmoDrawerRegistry.Warmup);
        EditorExtensionLifecycle.Invalidated += GizmoDrawerRegistry.Invalidate;
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            ScenePickingProviderRegistry.UnregisterAssembly(assembly);
            // Release Type, MethodInfo and compiled callback references before an unload,
            // even when no Scene window is open to refresh the reflection generation.
            GizmoDrawerRegistry.Invalidate();
            if (assembly != typeof(Rendering2DEditorModule).Assembly) return;
            EditorExtensionLifecycle.Invalidated -= GizmoDrawerRegistry.Invalidate;
            // A custom host can replace the policy; never clear its replacement.
            if (Tools.HandlePositionProvider == SceneHandleUtility.GetHandlePosition)
                Tools.HandlePositionProvider = null;
        });
    }
}
