using NVector2 = System.Numerics.Vector2;

namespace BEngine.Rendering;

/// <summary>Reusable fixed-point camera rotation and viewport scale for a collection of 2D vertices.</summary>
public readonly struct RenderViewportTransform2D : IEquatable<RenderViewportTransform2D>
{
    private readonly Vector2 _position;
    private readonly Fix64 _cosine, _sine;
    private readonly float _pixelsPerUnit, _centerX, _centerY;
    private readonly bool _alignedCamera;

    public bool Equals(RenderViewportTransform2D other) =>
        _position == other._position && _cosine == other._cosine && _sine == other._sine &&
        BitConverter.SingleToInt32Bits(_pixelsPerUnit) == BitConverter.SingleToInt32Bits(other._pixelsPerUnit) &&
        BitConverter.SingleToInt32Bits(_centerX) == BitConverter.SingleToInt32Bits(other._centerX) &&
        BitConverter.SingleToInt32Bits(_centerY) == BitConverter.SingleToInt32Bits(other._centerY) &&
        _alignedCamera == other._alignedCamera;
    public override bool Equals(object? obj) => obj is RenderViewportTransform2D other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_position, _cosine, _sine,
        BitConverter.SingleToInt32Bits(_pixelsPerUnit), BitConverter.SingleToInt32Bits(_centerX),
        BitConverter.SingleToInt32Bits(_centerY), _alignedCamera);

    public RenderViewportTransform2D(RenderCamera camera, int width, int height)
    {
        _position = camera.Position;
        var radians = -camera.Rotation * Fix64.Deg2Rad;
        var aligned = camera.Rotation % 360 == 0;
        _alignedCamera = aligned;
        _cosine = aligned ? Fix64.One : Fix64.Cos(radians);
        _sine = aligned ? Fix64.Zero : Fix64.Sin(radians);
        _pixelsPerUnit = camera.PixelsPerUnit(height);
        _centerX = camera.AlignViewportCenter ? Math.Max(1, width) / 2 : Math.Max(1, width) * .5f;
        _centerY = camera.AlignViewportCenter ? Math.Max(1, height) / 2 : Math.Max(1, height) * .5f;
    }

    public NVector2 WorldToViewport(Vector2 world)
    {
        var relative = world - _position;
        if (_alignedCamera)
            return new(_centerX + (float)relative.x * _pixelsPerUnit, _centerY - (float)relative.y * _pixelsPerUnit);
        var x = relative.x * _cosine - relative.y * _sine;
        var y = relative.x * _sine + relative.y * _cosine;
        return new(_centerX + (float)x * _pixelsPerUnit, _centerY - (float)y * _pixelsPerUnit);
    }
    public void FillQuadCorners(Span<NVector2> destination, SceneQuad2D quad)
    {
        if (quad.WorldAxisX is null && quad.WorldAxisY is null)
        { FillQuadCorners(destination, quad.Center, quad.Size, quad.Rotation); return; }
        if (destination.Length < 4) throw new ArgumentException("Four corners are required.", nameof(destination));
        quad.GetWorldAxes(out var x, out var y);
        destination[0] = WorldToViewport(quad.Center - x - y);
        destination[1] = WorldToViewport(quad.Center + x - y);
        destination[2] = WorldToViewport(quad.Center + x + y);
        destination[3] = WorldToViewport(quad.Center - x + y);
    }

    /// <summary>Writes the four quad corners in the same order as SceneQuad2D vertex colors.</summary>
    public void FillQuadCorners(Span<NVector2> destination, Vector2 center, Vector2 size, Fix64 rotation)
    {
        if (destination.Length < 4) throw new ArgumentException("Four corners are required.", nameof(destination));
        var half = size * Fix64.Half;
        var aligned = rotation % 360 == 0;
        if (aligned)
        {
            destination[0] = WorldToViewport(center + new Vector2(-half.x, -half.y));
            destination[1] = WorldToViewport(center + new Vector2(half.x, -half.y));
            destination[2] = WorldToViewport(center + new Vector2(half.x, half.y));
            destination[3] = WorldToViewport(center + new Vector2(-half.x, half.y));
            return;
        }
        var radians = rotation * Fix64.Deg2Rad;
        var cosine = aligned ? Fix64.One : Fix64.Cos(radians);
        var sine = aligned ? Fix64.Zero : Fix64.Sin(radians);
        var projection = this;
        destination[0] = Corner(-half.x, -half.y);
        destination[1] = Corner(half.x, -half.y);
        destination[2] = Corner(half.x, half.y);
        destination[3] = Corner(-half.x, half.y);

        NVector2 Corner(Fix64 x, Fix64 y) => projection.WorldToViewport(center + new Vector2(x * cosine - y * sine, x * sine + y * cosine));
    }
}
