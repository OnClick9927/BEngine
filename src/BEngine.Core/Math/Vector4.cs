namespace BEngine;

public readonly struct Vector4 : IEquatable<Vector4>
{
    public static readonly Vector4 zero = new(0, 0, 0, 0);
    public static readonly Vector4 one = new(1, 1, 1, 1);

    public Fix64 x { get; }
    public Fix64 y { get; }
    public Fix64 z { get; }
    public Fix64 w { get; }
    public Fix64 magnitude => Fix64.Sqrt(sqrMagnitude);
    public Fix64 sqrMagnitude => Dot(this, this);
    public Vector4 normalized => magnitude > Fix64.Epsilon ? this / magnitude : zero;

    public Vector4(Fix64 x, Fix64 y, Fix64 z, Fix64 w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public static Vector4 operator +(Vector4 left, Vector4 right) =>
        new(left.x + right.x, left.y + right.y, left.z + right.z, left.w + right.w);
    public static Vector4 operator -(Vector4 left, Vector4 right) =>
        new(left.x - right.x, left.y - right.y, left.z - right.z, left.w - right.w);
    public static Vector4 operator *(Vector4 value, Fix64 scale) =>
        new(value.x * scale, value.y * scale, value.z * scale, value.w * scale);
    public static Vector4 operator /(Vector4 value, Fix64 scale) =>
        new(value.x / scale, value.y / scale, value.z / scale, value.w / scale);
    public static bool operator ==(Vector4 left, Vector4 right) => left.Equals(right);
    public static bool operator !=(Vector4 left, Vector4 right) => !left.Equals(right);

    public static Fix64 Dot(Vector4 left, Vector4 right) =>
        left.x * right.x + left.y * right.y + left.z * right.z + left.w * right.w;
    public static Vector4 Lerp(Vector4 from, Vector4 to, Fix64 time) => from + (to - from) * Mathf.Clamp01(time);

    public bool Equals(Vector4 other) => x == other.x && y == other.y && z == other.z && w == other.w;
    public override bool Equals(object? obj) => obj is Vector4 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    public override string ToString() => $"({x}, {y}, {z}, {w})";
}
