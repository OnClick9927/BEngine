using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public readonly record struct RenderCamera(
    Vector2 Position,
    Fix64 Rotation,
    Fix64 Size,
    NVector4 ClearColor,
    CameraClearMode ClearMode,
    ulong CullingMask,
    Rect ViewportRect)
{
    public RenderCamera(Vector2 position, Fix64 rotation, Fix64 size, NVector4 clearColor)
        : this(position, rotation, size, clearColor, CameraClearMode.Color,
            SortingLayer.AllMask, new Rect(0, 0, 1, 1)) { }

    public static RenderCamera Default => new(Vector2.zero, Fix64.Zero, 5,
        new NVector4(0.055f, 0.071f, 0.09f, 1f));

    public static RenderCamera From(Camera2D camera) => new(
        camera.transform.position,
        camera.transform.rotation,
        camera.size,
        new NVector4((float)camera.backgroundColor.r, (float)camera.backgroundColor.g,
            (float)camera.backgroundColor.b, (float)camera.backgroundColor.a),
        camera.clearMode,
        camera.cullingMask,
        camera.viewportRect);

    public bool ContainsLayer(ulong layer) =>
        SortingLayer.IsValid(layer) && (CullingMask & layer) != 0;

    public bool IsVisible(Vector2 center, Fix64 rotation, Vector2 size, int width, int height)
    {
        var halfSize = new Vector2(Fix64.Abs(size.x), Fix64.Abs(size.y)) * Fix64.Half;
        var halfHeight = Fix64.Max(Fix64.Parse("0.001"), Size);
        var halfWidth = halfHeight * Math.Max(1, width) / Math.Max(1, height);
        var cameraLocal = Transform.RotateVector(center - Position, -Rotation);
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
        Transform.RotateVector(world - Position, -Rotation);

    public System.Numerics.Vector2 CameraToViewport(Vector2 relative, int width, int height)
    {
        var pixelsPerUnit = Math.Max(1, height) / Math.Max(0.002f, (float)Size * 2f);
        return new System.Numerics.Vector2(
            Math.Max(1, width) * 0.5f + (float)relative.x * pixelsPerUnit,
            Math.Max(1, height) * 0.5f - (float)relative.y * pixelsPerUnit);
    }

    public Vector2[] ViewBoundary(int width, int height)
    {
        var halfHeight = Fix64.Max(Fix64.Parse("0.001"), Size);
        var halfWidth = halfHeight * Math.Max(1, width) / Math.Max(1, height);
        return
        [
            CameraToWorld(new Vector2(-halfWidth, -halfHeight)),
            CameraToWorld(new Vector2(halfWidth, -halfHeight)),
            CameraToWorld(new Vector2(halfWidth, halfHeight)),
            CameraToWorld(new Vector2(-halfWidth, halfHeight))
        ];
    }

    public Vector2 CameraToWorld(Vector2 local) =>
        Position + Transform.RotateVector(local, Rotation);

    public float PixelsPerUnit(int height) =>
        Math.Max(1, height) / Math.Max(0.002f, (float)Size * 2f);
}
