namespace BEngine;

public sealed partial class Light2D
{
    public List<Vector2> shapePath { get; set; } = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
    public Fix64 falloffDistance { get; set; } = (Fix64).5;
    public Sprite? sprite { get; set; }
    public Vector2 spriteSize { get; set; } = new(2, 2);
    public Fix64 height { get; set; } = 1;

    public override void OnValidate() => ValidateShape();

    internal void ValidateShape()
    {
        if (!Enum.IsDefined(lightType)) throw new InvalidOperationException("Unknown 2D light type.");
        if (falloffDistance < 0 || height <= 0) throw new InvalidOperationException("Light falloff must be nonnegative and height must be positive.");
        if (lightType == Light2DType.Sprite && (spriteSize.x <= 0 || spriteSize.y <= 0))
            throw new InvalidOperationException("Sprite light size must be positive.");
        if (lightType != Light2DType.Freeform) return;
        if (shapePath is null || shapePath.Count is < 3 or > 128)
            throw new InvalidOperationException("A freeform light needs 3 to 128 vertices.");
        var area = Fix64.Zero;
        for (var index = 0; index < shapePath.Count; index++)
        {
            var a = shapePath[index]; var b = shapePath[(index + 1) % shapePath.Count];
            if ((a - b).sqrMagnitude <= Fix64.Epsilon) throw new InvalidOperationException("A freeform light has a zero-length edge.");
            area += Cross(a, b);
            for (var other = index + 2; other < shapePath.Count; other++)
            {
                if (index == 0 && other == shapePath.Count - 1) continue;
                var c = shapePath[other]; var d = shapePath[(other + 1) % shapePath.Count];
                var abC = Cross(b - a, c - a); var abD = Cross(b - a, d - a);
                var cdA = Cross(d - c, a - c); var cdB = Cross(d - c, b - c);
                if (((abC >= 0 && abD <= 0) || (abC <= 0 && abD >= 0)) &&
                    ((cdA >= 0 && cdB <= 0) || (cdA <= 0 && cdB >= 0)) &&
                    Fix64.Max(Fix64.Min(a.x, b.x), Fix64.Min(c.x, d.x)) <= Fix64.Min(Fix64.Max(a.x, b.x), Fix64.Max(c.x, d.x)) &&
                    Fix64.Max(Fix64.Min(a.y, b.y), Fix64.Min(c.y, d.y)) <= Fix64.Min(Fix64.Max(a.y, b.y), Fix64.Max(c.y, d.y)))
                    throw new InvalidOperationException("A freeform light must have a simple, nonintersecting outline.");
            }
        }
        if (Fix64.Abs(area) <= Fix64.Epsilon) throw new InvalidOperationException("A freeform light has no area.");
    }

    internal void FillWorldShape(List<Vector2> points)
    {
        ValidateShape(); points.Clear();
        foreach (var point in shapePath) points.Add(transform.TransformPoint(point));
    }

    private Fix64 ShapeAttenuation(Vector2 position)
    {
        var points = new List<Vector2>(); FillWorldShape(points);
        var inside = false; var distance = Fix64.FromRaw(long.MaxValue);
        for (var index = 0; index < points.Count; index++)
        {
            var a = points[index]; var b = points[(index + 1) % points.Count]; var edge = b - a;
            if (edge.sqrMagnitude <= Fix64.Epsilon) continue;
            distance = Fix64.Min(distance, Vector2.Distance(position, a + edge * Mathf.Clamp01(Vector2.Dot(position - a, edge) / edge.sqrMagnitude)));
            if ((a.y > position.y) != (b.y > position.y) && position.x < (b.x - a.x) * (position.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        if (inside || distance <= (Fix64).00001) return 1;
        if (falloffDistance <= 0) return 0;
        var attenuation = 1 - Mathf.Clamp01(distance / falloffDistance);
        return attenuation * attenuation;
    }

    internal Color SampleCookie(Vector2 position)
    {
        if (sprite is not { isDestroyed: false }) return new(0, 0, 0, 0);
        ValidateShape();
        if (transform.lossyScale.x == 0 || transform.lossyScale.y == 0) return new(0, 0, 0, 0);
        var local = transform.InverseTransformPoint(position); var visual = SpriteSourceRegistry.Resolve(sprite);
        var uv = new Vector2(local.x / spriteSize.x + visual.Pivot.x, 1 - visual.Pivot.y - local.y / spriteSize.y);
        if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return new(0, 0, 0, 0);
        var texture = visual.Texture == sprite.Texture ? sprite.texture : BAsset.Load<Texture>(visual.Texture);
        if (texture is null) return new(0, 0, 0, 0);
        if (!texture.isReadable) throw new InvalidOperationException("CPU Sprite light queries require a readable cookie texture; GPU rendering does not.");
        uv = new(visual.Uv.x + uv.x * visual.Uv.width, visual.Uv.y + uv.y * visual.Uv.height);
        return texture.GetPixel(Math.Clamp((int)(uv.x * texture.width), 0, texture.width - 1), Math.Clamp((int)(uv.y * texture.height), 0, texture.height - 1));
    }

    internal Color ColorAt(Vector2 position)
    {
        if (!enabled || !gameObject.activeInHierarchy || intensity <= 0) return new(0, 0, 0, 0);
        var cookie = lightType == Light2DType.Sprite ? SampleCookie(position) : Color.white;
        var factor = lightType == Light2DType.Sprite ? intensity * cookie.a : GetIntensityAt(position);
        return new(color.r * cookie.r * factor, color.g * cookie.g * factor, color.b * cookie.b * factor, 1);
    }

    private static Fix64 Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
