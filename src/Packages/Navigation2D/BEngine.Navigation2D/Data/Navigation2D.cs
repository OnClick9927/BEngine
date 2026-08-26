namespace BEngine.Navigation2D;

public static class Navigation2D
{
    public const ulong AllAreas = ulong.MaxValue;
    private static Scene? _scene;
    private static Scene? ActiveScene => SceneRuntime.currentScene ?? _scene;

    internal static void SetScene(Scene? scene) => _scene = scene;

    public static bool CalculatePath(Vector2 sourcePosition, Vector2 targetPosition,
        ulong areaMask, NavigationPath2D path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var scene = ActiveScene;
        if (scene is null)
        {
            path.ClearCorners();
            return false;
        }
        foreach (var surface in scene.QueryComponents<NavigationSurface2D>())
        {
            if (!surface.enabled || !surface.gameObject.activeInHierarchy || !surface.hasData) continue;
            if (surface.CalculatePath(sourcePosition, targetPosition, path)) return true;
        }
        path.ClearCorners();
        return false;
    }

    public static bool SamplePosition(Vector2 sourcePosition, out NavigationHit2D hit,
        Fix64 maxDistance, ulong areaMask = AllAreas)
    {
        if (ActiveScene is { } scene)
        {
            foreach (var surface in scene.QueryComponents<NavigationSurface2D>())
            {
                if (!surface.enabled || !surface.gameObject.activeInHierarchy || !surface.hasData) continue;
                if (surface.Sample(sourcePosition, maxDistance, out hit)) return true;
            }
        }
        hit = default;
        return false;
    }
}
