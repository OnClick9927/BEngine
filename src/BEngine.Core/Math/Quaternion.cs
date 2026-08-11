namespace BEngine;

public readonly struct Quaternion : IEquatable<Quaternion>
{
    public static readonly Quaternion identity = new(0, 0, 0, 1);

    public Fix64 x { get; }
    public Fix64 y { get; }
    public Fix64 z { get; }
    public Fix64 w { get; }

    public Quaternion(Fix64 x, Fix64 y, Fix64 z, Fix64 w)
    {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    public Quaternion normalized
    {
        get
        {
            var length = Fix64.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
            return length > Fix64.Epsilon
                ? new Quaternion(x / length, y / length, z / length, w / length)
                : identity;
        }
    }

    public static Quaternion Euler(Vector3 eulerAngles)
    {
        var halfX = eulerAngles.x * Fix64.Deg2Rad * Fix64.Half;
        var halfY = eulerAngles.y * Fix64.Deg2Rad * Fix64.Half;
        var halfZ = eulerAngles.z * Fix64.Deg2Rad * Fix64.Half;
        var sx = Fix64.Sin(halfX);
        var cx = Fix64.Cos(halfX);
        var sy = Fix64.Sin(halfY);
        var cy = Fix64.Cos(halfY);
        var sz = Fix64.Sin(halfZ);
        var cz = Fix64.Cos(halfZ);

        return new Quaternion(
            (sx * cy * cz) - (cx * sy * sz),
            (cx * sy * cz) + (sx * cy * sz),
            (cx * cy * sz) - (sx * sy * cz),
            (cx * cy * cz) + (sx * sy * sz)).normalized;
    }

    public static Quaternion Inverse(Quaternion value)
    {
        var lengthSquared = (value.x * value.x) + (value.y * value.y) +
                            (value.z * value.z) + (value.w * value.w);
        return lengthSquared > Fix64.Epsilon
            ? new Quaternion(-value.x / lengthSquared, -value.y / lengthSquared,
                -value.z / lengthSquared, value.w / lengthSquared)
            : identity;
    }

    public static Quaternion operator *(Quaternion left, Quaternion right) => new(
        (left.w * right.x) + (left.x * right.w) + (left.y * right.z) - (left.z * right.y),
        (left.w * right.y) - (left.x * right.z) + (left.y * right.w) + (left.z * right.x),
        (left.w * right.z) + (left.x * right.y) - (left.y * right.x) + (left.z * right.w),
        (left.w * right.w) - (left.x * right.x) - (left.y * right.y) - (left.z * right.z));

    public static Vector3 operator *(Quaternion rotation, Vector3 point)
    {
        var q = new Vector3(rotation.x, rotation.y, rotation.z);
        var uv = Vector3.Cross(q, point);
        var uuv = Vector3.Cross(q, uv);
        return point + (((rotation.w * uv) + uuv) * 2);
    }

    public static bool operator ==(Quaternion left, Quaternion right) => left.Equals(right);
    public static bool operator !=(Quaternion left, Quaternion right) => !left.Equals(right);
    public bool Equals(Quaternion other) => x == other.x && y == other.y && z == other.z && w == other.w;
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(x, y, z, w);
    public override string ToString() => $"({x}, {y}, {z}, {w})";
}
