namespace BEngine;

public readonly record struct SpriteSlice(Vector2 Center, Vector2 Size, Rect Uv);

/// <summary>Produces local geometry. UVs always stay inside the packed sprite rectangle.</summary>
public static class SpriteSliceGeometry
{
    public const int MaximumQuads = 65536;
    public static void Build(SpriteDrawMode mode, Vector2 size, Vector2 pivot, Vector2 pixels,
        Vector4 border, Fix64 pixelsPerUnit, Rect uv, bool flipX, bool flipY, List<SpriteSlice> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        output.Clear();
        var width = Fix64.Abs(size.x); var height = Fix64.Abs(size.y);
        if (width <= 0 || height <= 0) return;
        if (mode == SpriteDrawMode.Simple || pixels.x <= 0 || pixels.y <= 0)
        { output.Add(new(new((Fix64.Half - pivot.x) * width, (Fix64.Half - pivot.y) * height), new(width, height), uv)); return; }
        pixelsPerUnit = Fix64.Max(Fix64.Epsilon, pixelsPerUnit);
        var left = Mathf.Clamp(border.x, 0, pixels.x); var right = Mathf.Clamp(border.z, 0, pixels.x - left);
        var bottom = Mathf.Clamp(border.y, 0, pixels.y); var top = Mathf.Clamp(border.w, 0, pixels.y - bottom);
        var x = Axis(width, pixels.x, left, right, pixelsPerUnit, mode, flipX);
        // Local Y grows upwards; texture V grows downwards.
        var y = Axis(height, pixels.y, bottom, top, pixelsPerUnit, mode, flipY);
        if ((long)x.Count * y.Count > MaximumQuads)
            throw new InvalidOperationException("Sprite tiling exceeds the 65536-quad budget. Decrease pixelsPerUnit or reduce size.");
        foreach (var row in y) foreach (var column in x)
        {
            var u = flipX ? 1 - column.Source - column.SourceLength : column.Source;
            var v = flipY ? row.Source : 1 - row.Source - row.SourceLength;
            output.Add(new(new(column.Start + column.Length / 2 - width * pivot.x,
                row.Start + row.Length / 2 - height * pivot.y), new(column.Length, row.Length),
                new(uv.x + u * uv.width, uv.y + v * uv.height,
                    column.SourceLength * uv.width, row.SourceLength * uv.height)));
        }
    }

    private readonly record struct Segment(Fix64 Start, Fix64 Length, Fix64 Source, Fix64 SourceLength);
    private static List<Segment> Axis(Fix64 length, Fix64 pixels, Fix64 first, Fix64 last,
        Fix64 ppu, SpriteDrawMode mode, bool flip)
    {
        if (flip) (first, last) = (last, first);
        var a = first / ppu; var c = last / ppu;
        if (a + c >= length && a + c > 0) { a = length * first / (first + last); c = length - a; }
        var result = new List<Segment>();
        if (a > 0) result.Add(new(0, a, 0, first / pixels));
        var middle = length - a - c; var sourceMiddle = pixels - first - last;
        if (middle > 0 && sourceMiddle > 0)
        {
            var tile = mode == SpriteDrawMode.Tiled ? sourceMiddle / ppu : middle;
            var requested = Math.Ceiling((double)(middle / tile));
            if (requested > MaximumQuads) throw new InvalidOperationException("Sprite tiling exceeds the quad budget.");
            var count = (int)requested;
            for (var i = 0; i < count; i++)
            {
                var span = Fix64.Min(tile, middle - i * tile);
                result.Add(new(a + i * tile, span, first / pixels, sourceMiddle / pixels * span / tile));
            }
        }
        if (c > 0) result.Add(new(length - c, c, 1 - last / pixels, last / pixels));
        return result;
    }
}
