namespace BEngine.Navigation2D;

public static class Navigation2D
{
    public const ulong AllAreas = ulong.MaxValue;
    private static Scene? _scene;
    private static Scene? ActiveScene => SceneRuntime.currentScene ?? _scene;
    internal static long Revision { get; private set; }
    internal static void InvalidatePaths() => Revision++;

    internal static void SetScene(Scene? scene) => _scene = scene;

    public static bool CalculatePath(Vector2 sourcePosition, Vector2 targetPosition,
        ulong areaMask, NavigationPath2D path) => CalculatePath(sourcePosition, targetPosition, areaMask, path, null);

    internal static bool CalculatePath(Vector2 sourcePosition, Vector2 targetPosition,
        ulong areaMask, NavigationPath2D path, int? agentTypeId)
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
            if (agentTypeId.HasValue && surface.agentTypeId != agentTypeId.Value) continue;
            if (surface.CalculatePath(sourcePosition, targetPosition, path, areaMask)) return true;
        }
        path.ClearCorners();
        return false;
    }

    public static bool SamplePosition(Vector2 sourcePosition, out NavigationHit2D hit,
        Fix64 maxDistance, ulong areaMask = AllAreas)
        => SamplePosition(sourcePosition, out hit, maxDistance, areaMask, null);

    public static bool SamplePosition(Vector2 sourcePosition, out NavigationHit2D hit,
        Fix64 maxDistance, ulong areaMask, int agentTypeId)
        => SamplePosition(sourcePosition, out hit, maxDistance, areaMask, (int?)agentTypeId);

    private static bool SamplePosition(Vector2 sourcePosition, out NavigationHit2D hit,
        Fix64 maxDistance, ulong areaMask, int? agentTypeId)
    {
        hit = default;
        if (maxDistance < 0) return false;
        var found = false;
        if (ActiveScene is { } scene)
        {
            foreach (var surface in scene.QueryComponents<NavigationSurface2D>())
            {
                if (!surface.enabled || !surface.gameObject.activeInHierarchy || !surface.hasData) continue;
                if (agentTypeId.HasValue && surface.agentTypeId != agentTypeId.Value) continue;
                if (surface.Sample(sourcePosition, maxDistance, out var candidate, areaMask) &&
                    (!found || candidate.distance < hit.distance)) { hit = candidate; found = true; }
            }
        }
        return found;
    }
}
