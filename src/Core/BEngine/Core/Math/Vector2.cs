namespace BEngine;

public readonly struct Vector2 : IEquatable<Vector2>
{
    public static readonly Vector2 zero = new(0, 0);
    public static readonly Vector2 one = new(1, 1);
    public static readonly Vector2 up = new(0, 1);
    public static readonly Vector2 down = new(0, -1);
    public static readonly Vector2 right = new(1, 0);
    public static readonly Vector2 left = new(-1, 0);

    public Fix64 x { get; }
    public Fix64 y { get; }
    public Fix64 magnitude => Fix64.Sqrt(sqrMagnitude);
    public Fix64 sqrMagnitude => Dot(this, this);
    public Vector2 normalized => magnitude > Fix64.Epsilon ? this / magnitude : zero;

    public Vector2(Fix64 x, Fix64 y)
    {
        this.x = x;
        this.y = y;
    }

    public static Vector2 operator +(Vector2 left, Vector2 right) => new(left.x + right.x, left.y + right.y);
    public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.x - right.x, left.y - right.y);
    public static Vector2 operator -(Vector2 value) => new(-value.x, -value.y);
    public static Vector2 operator *(Vector2 value, Fix64 scale) => new(value.x * scale, value.y * scale);
    public static Vector2 operator *(Fix64 scale, Vector2 value) => value * scale;
    public static Vector2 operator /(Vector2 value, Fix64 scale) => new(value.x / scale, value.y / scale);
    public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);
    public static bool operator !=(Vector2 left, Vector2 right) => !left.Equals(right);

    public static Fix64 Dot(Vector2 left, Vector2 right) => left.x * right.x + left.y * right.y;
    public static Vector2 Scale(Vector2 left, Vector2 right) => new(left.x * right.x, left.y * right.y);
    public static Vector2 Lerp(Vector2 from, Vector2 to, Fix64 time) => from + (to - from) * Mathf.Clamp01(time);
    public static Vector2 LerpUnclamped(Vector2 from, Vector2 to, Fix64 time) => from + (to - from) * time;
    public static Fix64 Distance(Vector2 left, Vector2 right) => (left - right).magnitude;
    public static Fix64 Angle(Vector2 from, Vector2 to) =>
        Mathf.Atan2(from.x * to.y - from.y * to.x, Dot(from, to)) * Mathf.Rad2Deg;
    public static Vector2 Min(Vector2 left, Vector2 right) => new(Mathf.Min(left.x, right.x), Mathf.Min(left.y, right.y));
    public static Vector2 Max(Vector2 left, Vector2 right) => new(Mathf.Max(left.x, right.x), Mathf.Max(left.y, right.y));

    public bool Equals(Vector2 other) => x == other.x && y == other.y;
    public override bool Equals(object? obj) => obj is Vector2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(x, y);
    public override string ToString() => $"({x}, {y})";
}
