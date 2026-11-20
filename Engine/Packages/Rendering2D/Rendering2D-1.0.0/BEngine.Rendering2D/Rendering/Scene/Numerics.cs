using NMatrix3x2 = System.Numerics.Matrix3x2;
using NVector2 = System.Numerics.Vector2;
using NVector4 = System.Numerics.Vector4;

namespace BEngine.Rendering;

internal static class Numerics
{
    public static NVector2 ToNumerics(Vector2 value) => new((float)value.x, (float)value.y);
    public static NVector4 ToNumerics(Color value) =>
        new((float)value.r, (float)value.g, (float)value.b, (float)value.a);

    public static NMatrix3x2 WorldMatrix(Transform transform)
    {
        var position = ToNumerics(transform.localPosition);
        var scale = ToNumerics(transform.localScale);
        var rotation = (float)(transform.localRotation * Fix64.Deg2Rad);
        var local = NMatrix3x2.CreateScale(scale) * NMatrix3x2.CreateRotation(rotation) *
                    NMatrix3x2.CreateTranslation(position);
        return transform.parent is null ? local : local * WorldMatrix(transform.parent);
    }
}
