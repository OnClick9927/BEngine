namespace BEngine;

public readonly struct Vector3 : IEquatable<Vector3>
{
    public static readonly Vector3 zero = new(0, 0, 0);
    public static readonly Vector3 one = new(1, 1, 1);
    public static readonly Vector3 up = new(0, 1, 0);
    public static readonly Vector3 down = new(0, -1, 0);
    public static readonly Vector3 right = new(1, 0, 0);
    public static readonly Vector3 left = new(-1, 0, 0);
    public static readonly Vector3 forward = new(0, 0, 1);
    public static readonly Vector3 back = new(0, 0, -1);

    public Fix64 x { get; }
    public Fix64 y { get; }
    public Fix64 z { get; }
    public Fix64 magnitude => Fix64.Sqrt(sqrMagnitude);
    public Fix64 sqrMagnitude => Dot(this, this);
    public Vector3 normalized => magnitude > Fix64.Epsilon ? this / magnitude : zero;

    public Vector3(Fix64 x, Fix64 y, Fix64 z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static Vector3 operator +(Vector3 left, Vector3 right) =>
        new(left.x + right.x, left.y + right.y, left.z + right.z);
    public static Vector3 operator -(Vector3 left, Vector3 right) =>
        new(left.x - right.x, left.y - right.y, left.z - right.z);
    public static Vector3 operator -(Vector3 value) => new(-value.x, -value.y, -value.z);
    public static Vector3 operator *(Vector3 value, Fix64 scale) =>
        new(value.x * scale, value.y * scale, value.z * scale);
    public static Vector3 operator *(Fix64 scale, Vector3 value) => value * scale;
    public static Vector3 operator /(Vector3 value, Fix64 scale) =>
        new(value.x / scale, value.y / scale, value.z / scale);
    public static bool operator ==(Vector3 left, Vector3 right) => left.Equals(right);
    public static bool operator !=(Vector3 left, Vector3 right) => !left.Equals(right);

    public static Fix64 Dot(Vector3 left, Vector3 right) =>
        (left.x * right.x) + (left.y * right.y) + (left.z * right.z);
    public static Vector3 Cross(Vector3 left, Vector3 right) => new(
        (left.y * right.z) - (left.z * right.y),
        (left.z * right.x) - (left.x * right.z),
        (left.x * right.y) - (left.y * right.x));
    public static Vector3 Scale(Vector3 left, Vector3 right) =>
        new(left.x * right.x, left.y * right.y, left.z * right.z);
    public static Vector3 Lerp(Vector3 from, Vector3 to, Fix64 time) =>
        from + ((to - from) * Mathf.Clamp01(time));
    public static Fix64 Distance(Vector3 left, Vector3 right) => (left - right).magnitude;
    public static Vector3 MoveTowards(Vector3 current, Vector3 target, Fix64 maxDistanceDelta)
    {
        var delta = target - current;
        var distance = delta.magnitude;
        return distance <= maxDistanceDelta || distance == 0
            ? target
            : current + delta / distance * maxDistanceDelta;
    }

    public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
    public override bool Equals(object? obj) => obj is Vector3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(x, y, z);
    public override string ToString() => $"({x}, {y}, {z})";
}
