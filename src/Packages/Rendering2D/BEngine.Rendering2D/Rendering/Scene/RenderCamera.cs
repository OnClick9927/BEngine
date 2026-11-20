using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

[RuntimeTypeAlias("BEngine.Rendering.RenderCamera, BEngine.Graphics")]
[RuntimeTypeAlias("BEngine.Rendering.RenderCamera, BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public readonly record struct RenderCamera(
    Vector2 Position,
    Fix64 Rotation,
    Fix64 Size,
    NVector4 ClearColor,
    CameraClearMode ClearMode,
    ulong CullingMask,
    Rect ViewportRect)
{
    private static readonly Fix64 MinimumHalfHeight = Fix64.FromRaw(4294967L) /* Parse("0.001") */;
    public IRenderCameraPolicy? Policy { get; init; }
    public bool AlignViewportCenter => Policy?.AlignViewportCenter ?? false;

    public RenderCamera(Vector2 position, Fix64 rotation, Fix64 size, NVector4 clearColor)
        : this(position, rotation, size, clearColor, CameraClearMode.Color,
            SortingLayer.AllMask, new Rect(0, 0, 1, 1)) { }

    public static RenderCamera Default => new(Vector2.zero, Fix64.Zero, 5,
        new NVector4(0.055f, 0.071f, 0.09f, 1f));

    public RenderCamera ForViewport(int width, int height) => Policy?.ForViewport(this, width, height) ?? this;
    public RenderViewportTransform2D CreateViewportTransform(int width, int height) => new(this, width, height);

    public Vector2 SnapWorldPoint(Vector2 point) => Policy?.SnapWorldPoint(point) ?? point;

    public bool ContainsLayer(ulong layer) => RenderLayerMask.Contains(CullingMask, layer);

    public bool IsVisible(Vector2 center, Fix64 rotation, Vector2 size, int width, int height)
    {
        var halfSize = new Vector2(Fix64.Abs(size.x), Fix64.Abs(size.y)) * Fix64.Half;
        var halfHeight = Fix64.Max(MinimumHalfHeight, Size);
        var halfWidth = halfHeight * Math.Max(1, width) / Math.Max(1, height);
        var cameraLocal = Vector2.Rotate(center - Position, -Rotation);
        var relativeRadians = (rotation - Rotation) * Fix64.Deg2Rad;
        var cosine = Fix64.Abs(Fix64.Cos(relativeRadians));
        var sine = Fix64.Abs(Fix64.Sin(relativeRadians));
        var extentX = cosine * halfSize.x + sine * halfSize.y;
        var extentY = sine * halfSize.x + cosine * halfSize.y;
        return cameraLocal.x + extentX >= -halfWidth && cameraLocal.x - extentX <= halfWidth &&
               cameraLocal.y + extentY >= -halfHeight && cameraLocal.y - extentY <= halfHeight;
    }

    public System.Numerics.Vector2 WorldToViewport(Vector2 world, int width, int height)
    {
        var relative = WorldToCamera(world);
        return CameraToViewport(relative, width, height);
    }

    public Vector2 WorldToCamera(Vector2 world) =>
        Vector2.Rotate(world - Position, -Rotation);

    public System.Numerics.Vector2 CameraToViewport(Vector2 relative, int width, int height)
    {
        var pixelsPerUnit = Math.Max(1, height) / Math.Max(0.002f, (float)Size * 2f);
        return new System.Numerics.Vector2(
            (AlignViewportCenter ? Math.Max(1, width) / 2 : Math.Max(1, width) * 0.5f) + (float)relative.x * pixelsPerUnit,
            (AlignViewportCenter ? Math.Max(1, height) / 2 : Math.Max(1, height) * 0.5f) - (float)relative.y * pixelsPerUnit);
    }

    public Vector2[] ViewBoundary(int width, int height)
    {
        var result = new Vector2[4];
        FillViewBoundary(result, width, height);
        return result;
    }

    /// <summary>Writes the four world-space viewport corners to reusable storage.</summary>
    public void FillViewBoundary(Span<Vector2> destination, int width, int height, Fix64 padding = default)
    {
        if (destination.Length < 4) throw new ArgumentException("Four corners are required.", nameof(destination));
        if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
        var halfHeight = Fix64.Max(MinimumHalfHeight, Size);
        var halfWidth = halfHeight * Math.Max(1, width) / Math.Max(1, height);
        halfHeight += padding; halfWidth += padding;
        destination[0] = CameraToWorld(new Vector2(-halfWidth, -halfHeight));
        destination[1] = CameraToWorld(new Vector2(halfWidth, -halfHeight));
        destination[2] = CameraToWorld(new Vector2(halfWidth, halfHeight));
        destination[3] = CameraToWorld(new Vector2(-halfWidth, halfHeight));
    }

    public Vector2 CameraToWorld(Vector2 local) =>
        Position + Vector2.Rotate(local, Rotation);

    public float PixelsPerUnit(int height) =>
        Math.Max(1, height) / Math.Max(0.002f, (float)Size * 2f);
}
