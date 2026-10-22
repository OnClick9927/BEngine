namespace BEngine.Editor;

internal static class SceneGizmoPass
{
    [ThreadStatic]
    private static GizmoViewport _viewport;

    internal static int currentViewportWidth => Math.Max(1, _viewport.Width);
    internal static int currentViewportHeight => Math.Max(1, _viewport.Height);

    internal static GizmoDrawList Collect(
        IReadOnlyList<Scene> scenes,
        GameObject? selected,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        var drawList = new GizmoDrawList();
        if (!SceneGizmoVisibility.Enabled) return drawList;
        using var viewportScope = new GizmoViewportScope(
            new GizmoViewport(Math.Max(1, viewportWidth), Math.Max(1, viewportHeight)));
        using var collection = Gizmos.BeginCollection(drawList);
        foreach (var scene in scenes)
        {
            if (scene is null || !scene.isCreated || !scene.isLoaded) continue;
            foreach (var gameObject in scene.gameObjects)
            {
                if (SceneVisibilityManager.instance.IsHidden(gameObject)) continue;
                var isSelected = ReferenceEquals(gameObject, selected);
                var state = isSelected
                    ? GizmoType.Selected | GizmoType.InSelectionHierarchy
                    : GizmoType.NonSelected | GizmoType.NotInSelectionHierarchy;
                if (gameObject.activeInHierarchy) state |= GizmoType.Active;
                foreach (var component in gameObject.components)
                {
                    var componentType = component.GetType();
                    if (!SceneGizmoVisibility.IsVisible(componentType)) continue;
                    InvokeComponentCallback(component, isSelected);
                    InvokeDrawers(component, state);
                }
            }
        }
        return drawList;
    }

    private static void InvokeComponentCallback(Component component, bool selected)
    {
        Gizmos.ResetState();
        var callbackName = selected
            ? nameof(Component.OnDrawGizmosSelected)
            : nameof(Component.OnDrawGizmos);
        EditorFeatureGuard.Invoke(component, callbackName, selected
            ? component.OnDrawGizmosSelected
            : component.OnDrawGizmos);
    }

    private static void InvokeDrawers(Component component, GizmoType state)
    {
        foreach (var drawer in GizmoDrawerRegistry.GetDrawers(component.GetType()))
        {
            if (!GizmoDrawerRegistry.ShouldInvoke(drawer, state)) continue;
            Gizmos.ResetState();
            EditorFeatureGuard.Invoke(drawer.Feature, () => drawer.Callback(component, state));
        }
    }

    private readonly record struct GizmoViewport(int Width, int Height);

    private readonly struct GizmoViewportScope : IDisposable
    {
        private readonly GizmoViewport _previous;

        internal GizmoViewportScope(GizmoViewport current)
        {
            _previous = _viewport;
            _viewport = current;
        }

        public void Dispose() => _viewport = _previous;
    }
}
