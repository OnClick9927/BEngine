using BEngine.Rendering;
using NVector2 = System.Numerics.Vector2;

namespace BEngine.Editor;

public static class ScenePickingUtility
{
    internal static IReadOnlyList<GameObject> PickAll(
        IReadOnlyList<Scene> scenes,
        RenderCamera camera,
        Vector2 viewportPoint,
        int viewportWidth,
        int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        viewportWidth = Math.Max(1, viewportWidth);
        viewportHeight = Math.Max(1, viewportHeight);
        var visibility = SceneVisibilityManager.instance;
        var candidates = new List<ScenePickCandidate>();
        long sequence = 0;

        foreach (var scene in scenes)
        {
            if (scene is null || !scene.isCreated || !scene.isLoaded) continue;
            var hierarchy = HierarchyOrder2D.Build(scene);
            foreach (var renderer in scene.QueryComponents<SpriteRenderer>())
            {
                if (!CanPick(renderer, camera, visibility)) continue;
                var visual = renderer.ResolveSpriteUnchecked();
                var pivot = renderer.useAtlasPivot && !string.IsNullOrWhiteSpace(renderer.atlas)
                    ? visual.Pivot
                    : renderer.pivot;
                var localCenter = new Vector2(
                    (Fix64.Half - pivot.x) * renderer.size.x,
                    (Fix64.Half - pivot.y) * renderer.size.y);
                var center = renderer.transform.TransformPoint(localCenter);
                var renderedSize = Abs(Vector2.Scale(renderer.size, renderer.transform.lossyScale));
                if (!camera.IsVisible(center, renderer.transform.rotation, renderedSize,
                        viewportWidth, viewportHeight)) continue;
                var key = new RenderSortKey2D(renderer.sortingLayer, renderer.orderInLayer,
                    hierarchy.GetValueOrDefault(renderer.gameObject),
                    renderer.TransparencyUnchecked(renderer.color), sequence++);
                if (renderer.opacity <= Fix64.Zero || renderer.color.a <= Fix64.Zero ||
                    !ContainsQuad(viewportPoint, center, renderer.transform.rotation, renderedSize,
                        camera, viewportWidth, viewportHeight)) continue;
                candidates.Add(new ScenePickCandidate(renderer.gameObject, key));
            }

            foreach (var system in scene.QueryComponents<ParticleSystem2D>())
            {
                if (!CanPick(system, camera, visibility)) continue;
                var hierarchyOrder = hierarchy.GetValueOrDefault(system.gameObject);
                var visual = system.ResolveSpriteUnchecked();
                foreach (var particle in system.particles)
                {
                    var worldPosition = system.transform.TransformPoint(particle.Position);
                    var color = new Color(particle.Color.r, particle.Color.g, particle.Color.b,
                        particle.Color.a * system.opacity);
                    var rotation = system.transform.rotation + particle.Rotation;
                    var offset = new Vector2(
                        (Fix64.Half - visual.Pivot.x) * particle.Size.x,
                        (Fix64.Half - visual.Pivot.y) * particle.Size.y);
                    worldPosition += Transform.RotateVector(
                        Vector2.Scale(offset, system.transform.lossyScale), rotation);
                    var renderedSize = Abs(Vector2.Scale(particle.Size, system.transform.lossyScale));
                    if (!camera.IsVisible(worldPosition, rotation, renderedSize,
                            viewportWidth, viewportHeight)) continue;
                    var key = new RenderSortKey2D(system.sortingLayer, system.orderInLayer,
                        hierarchyOrder, system.TransparencyUnchecked(color), sequence++);
                    if (color.a <= Fix64.Zero ||
                        !ContainsQuad(viewportPoint, worldPosition, rotation, renderedSize,
                            camera, viewportWidth, viewportHeight)) continue;
                    candidates.Add(new ScenePickCandidate(system.gameObject, key));
                }
            }

            sequence = ScenePickingProviderRegistry.Collect(
                scene, camera, viewportPoint, viewportWidth, viewportHeight,
                candidates, sequence,
                gameObject => visibility.IsVisible(gameObject) &&
                              !visibility.IsPickingDisabled(gameObject));
        }

        var seen = new HashSet<GameObject>(ReferenceEqualityComparer.Instance);
        return candidates
            .OrderByDescending(candidate => candidate.SortKey)
            .Where(candidate => seen.Add(candidate.GameObject))
            .Select(candidate => candidate.GameObject)
            .ToArray();
    }

    private static bool CanPick(Renderer2D renderer, RenderCamera camera,
        SceneVisibilityManager visibility) =>
        renderer.enabled && renderer.gameObject.activeInHierarchy &&
        camera.ContainsLayer(renderer.sortingLayer) &&
        visibility.IsVisible(renderer.gameObject) &&
        !visibility.IsPickingDisabled(renderer.gameObject);

    public static bool ContainsQuad(Vector2 point, Vector2 center, Fix64 rotation, Vector2 size,
        RenderCamera camera, int width, int height)
    {
        if (size.x <= Fix64.Zero || size.y <= Fix64.Zero) return false;
        var half = size * Fix64.Half;
        var local = new[]
        {
            new Vector2(-half.x, -half.y),
            new Vector2(half.x, -half.y),
            new Vector2(half.x, half.y),
            new Vector2(-half.x, half.y)
        };
        var corners = local.Select(value => camera.WorldToViewport(
            center + Transform.RotateVector(value, rotation), width, height)).ToArray();
        var target = new NVector2((float)point.x, (float)point.y);
        return InTriangle(target, corners[0], corners[1], corners[2]) ||
               InTriangle(target, corners[0], corners[2], corners[3]);
    }

    private static bool InTriangle(NVector2 point, NVector2 a, NVector2 b, NVector2 c)
    {
        var ab = Cross(b - a, point - a);
        var bc = Cross(c - b, point - b);
        var ca = Cross(a - c, point - c);
        const float epsilon = 0.001f;
        return (ab >= -epsilon && bc >= -epsilon && ca >= -epsilon) ||
               (ab <= epsilon && bc <= epsilon && ca <= epsilon);
    }

    private static float Cross(NVector2 left, NVector2 right) =>
        left.X * right.Y - left.Y * right.X;

    private static Vector2 Abs(Vector2 value) =>
        new(Fix64.Abs(value.x), Fix64.Abs(value.y));
}
