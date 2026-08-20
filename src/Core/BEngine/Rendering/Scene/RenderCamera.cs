using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public readonly record struct RenderCamera(
    Vector2 Position,
    Fix64 Rotation,
    Fix64 Size,
    NVector4 ClearColor)
{
    public static RenderCamera Default => new(Vector2.zero, Fix64.Zero, 5,
        new NVector4(0.055f, 0.071f, 0.09f, 1f));

    public static RenderCamera From(Camera2D camera) => new(
        camera.transform.position,
        camera.transform.rotation,
        camera.size,
        new NVector4((float)camera.backgroundColor.r, (float)camera.backgroundColor.g,
            (float)camera.backgroundColor.b, (float)camera.backgroundColor.a));

    public System.Numerics.Vector2 WorldToViewport(Vector2 world, int width, int height)
    {
        var relative = Transform.RotateVector(world - Position, -Rotation);
        var pixelsPerUnit = Math.Max(1, height) / Math.Max(0.002f, (float)Size * 2f);
        return new System.Numerics.Vector2(
            Math.Max(1, width) * 0.5f + (float)relative.x * pixelsPerUnit,
            Math.Max(1, height) * 0.5f - (float)relative.y * pixelsPerUnit);
    }

    public float PixelsPerUnit(int height) =>
        Math.Max(1, height) / Math.Max(0.002f, (float)Size * 2f);
}
