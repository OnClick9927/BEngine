using BEngine.Rendering;
using NVector2 = System.Numerics.Vector2;

namespace BEngine.Editor;

[RuntimeTypeAlias("BEngine.Editor.ScenePickingUtility, BEngine.EditorWorkbench.Editor")]
[RuntimeTypeAlias("BEngine.Editor.ScenePickingUtility, BEngine.EditorWorkbench.Editor, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public static class ScenePickingUtility
{
    /// <summary>Returns visible Scene objects under the pointer, in front-to-back order.</summary>
    public static IReadOnlyList<GameObject> PickAll(
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
                var pivot = renderer.useSpritePivot && renderer.sprite is not null
                    ? visual.Pivot : renderer.pivot;
                var localCenter = new Vector2(
                    (Fix64.Half - pivot.x) * renderer.size.x,
                    (Fix64.Half - pivot.y) * renderer.size.y);
                var center = renderer.transform.TransformPoint(localCenter);
                var renderedSize = Abs(Vector2.Scale(renderer.size, renderer.transform.lossyScale));
                if (!camera.IsVisible(center, renderer.transform.rotation, renderedSize,
                        viewportWidth, viewportHeight)) continue;
                var key = new RenderOrderKey(renderer.sortingLayer, renderer.orderInLayer,
                    hierarchy.GetValueOrDefault(renderer.gameObject),
                    renderer.TransparencyUnchecked(renderer.color), sequence++);
                if (renderer.opacity <= Fix64.Zero || renderer.color.a <= Fix64.Zero ||
                    !ContainsQuad(viewportPoint, center, renderer.transform.rotation, renderedSize,
                        camera, viewportWidth, viewportHeight)) continue;
                candidates.Add(new ScenePickCandidate(renderer.gameObject, key));
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
        var projectionSource3811 = local;
        ArgumentNullException.ThrowIfNull(projectionSource3811, "source");
        var projectionResult3811 = projectionSource3811.Length == 0 ? global::System.Array.Empty<global::System.Numerics.Vector2>() : new global::System.Numerics.Vector2[projectionSource3811.Length];
        for (var projectionIndex3811 = 0; projectionIndex3811 < projectionSource3811.Length; projectionIndex3811++)
        {
            var projectionItem3811 = projectionSource3811[projectionIndex3811];
            projectionResult3811[projectionIndex3811] = camera.WorldToViewport(center + Vector2.Rotate(projectionItem3811, rotation), width, height);
        }
        
        var corners = projectionResult3811;
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
