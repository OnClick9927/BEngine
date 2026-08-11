using NMatrix4x4 = System.Numerics.Matrix4x4;
using NQuaternion = System.Numerics.Quaternion;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

public readonly record struct RenderCamera(
    NVector3 Position,
    NQuaternion Rotation,
    float FieldOfView,
    float NearPlane,
    float FarPlane,
    NVector4 ClearColor,
    BEngine.CameraClearFlags ClearFlags)
{
    public static RenderCamera Default => new(
        new NVector3(5f, 4f, -7f),
        NQuaternion.CreateFromYawPitchRoll(-0.6f, -0.35f, 0f),
        MathF.PI / 3f,
        0.1f,
        1000f,
        new NVector4(0.055f, 0.071f, 0.09f, 1f),
        BEngine.CameraClearFlags.Skybox);

    public static RenderCamera From(BEngine.Camera camera) => new(
        Numerics.ToNumerics(camera.transform.position),
        Numerics.ToNumerics(camera.transform.rotation),
        (float)camera.fieldOfView * MathF.PI / 180f,
        (float)camera.nearClipPlane,
        (float)camera.farClipPlane,
        Numerics.ToNumerics(camera.backgroundColor),
        camera.clearFlags);
}

internal static class Numerics
{
    public static NVector3 ToNumerics(BEngine.Vector3 value) =>
        new((float)value.x, (float)value.y, (float)value.z);

    public static NQuaternion ToNumerics(BEngine.Quaternion value) =>
        NQuaternion.Normalize(new((float)value.x, (float)value.y, (float)value.z, (float)value.w));

    public static NVector4 ToNumerics(BEngine.Color value) =>
        new((float)value.r, (float)value.g, (float)value.b, (float)value.a);

    public static NMatrix4x4 WorldMatrix(BEngine.Transform transform)
    {
        var local = NMatrix4x4.CreateScale(ToNumerics(transform.localScale)) *
                    NMatrix4x4.CreateFromQuaternion(ToNumerics(transform.localRotation)) *
                    NMatrix4x4.CreateTranslation(ToNumerics(transform.localPosition));
        return transform.parent is null ? local : local * WorldMatrix(transform.parent);
    }
}
