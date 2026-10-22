namespace BEngine;

/// <summary>Fixed-point affine transform. Points are column vectors; parent * local composes a hierarchy.</summary>
public readonly record struct AffineTransform2D(Fix64 M11, Fix64 M12, Fix64 M21, Fix64 M22, Fix64 Tx, Fix64 Ty)
{
    public static AffineTransform2D identity => new(1, 0, 0, 1, 0, 0);
    public Vector2 translation => new(Tx, Ty);
    public Vector2 TransformPoint(Vector2 point) => TransformVector(point) + translation;
    public Vector2 TransformVector(Vector2 vector) => new(M11 * vector.x + M12 * vector.y, M21 * vector.x + M22 * vector.y);
    public static AffineTransform2D TRS(Vector2 position, Fix64 rotation, Vector2 scale)
    {
        var x = Transform.RotateVector(Vector2.right, rotation);
        return new(x.x * scale.x, -x.y * scale.y, x.y * scale.x, x.x * scale.y, position.x, position.y);
    }
    public static AffineTransform2D operator *(AffineTransform2D a, AffineTransform2D b) => new(
        a.M11 * b.M11 + a.M12 * b.M21, a.M11 * b.M12 + a.M12 * b.M22,
        a.M21 * b.M11 + a.M22 * b.M21, a.M21 * b.M12 + a.M22 * b.M22,
        a.M11 * b.Tx + a.M12 * b.Ty + a.Tx, a.M21 * b.Tx + a.M22 * b.Ty + a.Ty);
    public bool TryInverse(out AffineTransform2D inverse)
    {
        try
        {
        var scale = Fix64.Max(Fix64.Max(Fix64.Abs(M11), Fix64.Abs(M12)), Fix64.Max(Fix64.Abs(M21), Fix64.Abs(M22)));
        if (scale == 0) { inverse = default; return false; }
        var a = M11 / scale; var b = M12 / scale; var c = M21 / scale; var d = M22 / scale;
        var determinant = a * d - b * c;
        if (determinant == 0) { inverse = default; return false; }
        inverse = WithInverseTranslation(d / determinant / scale, -b / determinant / scale, -c / determinant / scale, a / determinant / scale);
        return true;
        }
        // Rounded rank-one matrices can have a one-raw-unit determinant. Their
        // apparent inverse overflows Fix64; treat it like the singular projection path.
        catch (OverflowException) { inverse = default; return false; }
    }
    /// <summary>For singular transforms, returns the minimum-norm inverse projection instead of dividing by zero.</summary>
    public AffineTransform2D InverseOrProjection()
    {
        if (TryInverse(out var inverse)) return inverse;
        var scale = Fix64.Max(Fix64.Max(Fix64.Abs(M11), Fix64.Abs(M12)), Fix64.Max(Fix64.Abs(M21), Fix64.Abs(M22)));
        if (scale == 0) return default;
        var a = M11 / scale; var b = M12 / scale; var c = M21 / scale; var d = M22 / scale;
        var norm = a * a + b * b + c * c + d * d;
        return WithInverseTranslation(a / norm / scale, c / norm / scale, b / norm / scale, d / norm / scale);
    }
    private AffineTransform2D WithInverseTranslation(Fix64 a, Fix64 b, Fix64 c, Fix64 d) =>
        new(a, b, c, d, -(a * Tx + b * Ty), -(c * Tx + d * Ty));
}
