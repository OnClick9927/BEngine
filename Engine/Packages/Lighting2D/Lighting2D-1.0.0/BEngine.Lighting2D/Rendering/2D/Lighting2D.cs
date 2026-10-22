namespace BEngine.Rendering;

public static class Lighting2D
{
    public static Color Evaluate(Scene scene, Vector2 worldPosition, ulong targetLayer, Color albedo)
    {
        ArgumentNullException.ThrowIfNull(scene);
        // Scene.QueryComponents reuses read-only snapshots without allocating a
        // new list or copying every component for this point query.
        var lights = scene.QueryComponents<Light2D>();
        var casters = scene.QueryComponents<ShadowCaster2D>();
        var environments = scene.QueryComponents<LightingEnvironment2D>();
        return Evaluate(worldPosition, targetLayer, null, albedo, lights, casters, environments);
    }

    internal static Color Evaluate(
        Vector2 worldPosition,
        ulong targetLayer,
        GameObject? target,
        Color albedo,
        IReadOnlyList<Light2D> lights,
        IReadOnlyList<ShadowCaster2D> casters,
        IReadOnlyList<LightingEnvironment2D> environments)
    {
        var hasLighting = false;
        var red = Fix64.Zero;
        var green = Fix64.Zero;
        var blue = Fix64.Zero;

        for (var index = 0; index < environments.Count; index++)
        {
            var environment = environments[index];
            if (!environment.enabled || !environment.gameObject.activeInHierarchy) continue;
            hasLighting = true;
            red += environment.ambientColor.r * environment.ambientIntensity;
            green += environment.ambientColor.g * environment.ambientIntensity;
            blue += environment.ambientColor.b * environment.ambientIntensity;
        }

        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            if (!light.enabled || !light.gameObject.activeInHierarchy ||
                !light.targetLayers.Contains(targetLayer)) continue;
            hasLighting = true;
            var contribution = light.ColorAt(worldPosition);
            var visibility = Fix64.One;
            if (light.castShadows && light.lightType != Light2DType.Global &&
                IsOccluded(light, worldPosition, target, casters))
                visibility = Fix64.One - light.shadowStrength;
            red += contribution.r * visibility;
            green += contribution.g * visibility;
            blue += contribution.b * visibility;
        }

        if (!hasLighting) return albedo;
        return new Color(
            Fix64.Clamp(albedo.r * red, Fix64.Zero, Fix64.One),
            Fix64.Clamp(albedo.g * green, Fix64.Zero, Fix64.One),
            Fix64.Clamp(albedo.b * blue, Fix64.Zero, Fix64.One),
            albedo.a);
    }

    private static bool IsOccluded(
        Light2D light,
        Vector2 point,
        GameObject? target,
        IReadOnlyList<ShadowCaster2D> casters)
    {
        if (casters.Count == 0) return false;
        var origin = light.transform.position;
        if ((point - origin).sqrMagnitude <= Fix64.Epsilon) return false;
        var path = new List<Vector2>();
        for (var casterIndex = 0; casterIndex < casters.Count; casterIndex++)
        {
            var caster = casters[casterIndex];
            if (!caster.enabled || !caster.gameObject.activeInHierarchy || !caster.castsShadows ||
                ReferenceEquals(caster.gameObject, target) ||
                !light.shadowCasterLayers.Contains(caster.gameObject.layer) ||
                !caster.layerMask.Contains(light.gameObject.layer)) continue;
            caster.FillWorldPath(path);
            if (path.Count < 2) continue;
            for (var index = 0; index < path.Count; index++)
            {
                if (SegmentsIntersect(origin, point, path[index], path[(index + 1) % path.Count]))
                    return true;
            }
        }
        return false;
    }

    private static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var ray = b - a;
        var edge = d - c;
        var denominator = Cross(ray, edge);
        if (Fix64.Abs(denominator) <= Fix64.Epsilon) return false;
        var offset = c - a;
        var rayTime = Cross(offset, edge) / denominator;
        var edgeTime = Cross(offset, ray) / denominator;
        var epsilon = Fix64.Epsilon * 16;
        return rayTime > epsilon && rayTime < Fix64.One - epsilon &&
               edgeTime >= Fix64.Zero && edgeTime <= Fix64.One;
    }

    private static Fix64 Cross(Vector2 left, Vector2 right) =>
        left.x * right.y - left.y * right.x;
}
