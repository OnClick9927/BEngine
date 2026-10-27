namespace BEngine;

public readonly struct Vector2 : IEquatable<Vector2>
{
    public static Vector2 ClampMagnitude(Vector2 value, Fix64 maximum)
    {
        maximum = Fix64.Max(0, maximum);
        var length = value.magnitude;
        return length > maximum && length > Fix64.Epsilon ? value * (maximum / length) : value;
    }
    public static readonly Vector2 zero = new(0, 0);
    public static readonly Vector2 one = new(1, 1);
    public static readonly Vector2 up = new(0, 1);
    public static readonly Vector2 down = new(0, -1);
    public static readonly Vector2 right = new(1, 0);
    public static readonly Vector2 left = new(-1, 0);

    public Fix64 x { get; }
    public Fix64 y { get; }
    public Fix64 magnitude => Fix64.Sqrt(sqrMagnitude);
    public Fix64 sqrMagnitude
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!OperatingSystem.IsBrowser()) return Dot(this, this);
            var xHigh = Math.BigMul(x.RawValue, x.RawValue, out var xLow);
            _ = checked((int)xHigh);
            var xRaw = unchecked((long)(((ulong)xHigh << Fix64.FractionalBits) |
                ((ulong)xLow >> Fix64.FractionalBits)));
            var yHigh = Math.BigMul(y.RawValue, y.RawValue, out var yLow);
            _ = checked((int)yHigh);
            var yRaw = unchecked((long)(((ulong)yHigh << Fix64.FractionalBits) |
                ((ulong)yLow >> Fix64.FractionalBits)));
            return Fix64.FromRaw(checked(xRaw + yRaw));
        }
    }
    public Vector2 normalized
    {
        get
        {
            var length = magnitude;
            return length > Fix64.Epsilon ? this / length : zero;
        }
    }

    public Vector2(Fix64 x, Fix64 y)
    {
        this.x = x;
        this.y = y;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator +(Vector2 left, Vector2 right) => new(left.x + right.x, left.y + right.y);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 left, Vector2 right) => new(left.x - right.x, left.y - right.y);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator -(Vector2 value) => new(-value.x, -value.y);
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator *(Vector2 value, Fix64 scale)
    {
        if (!OperatingSystem.IsBrowser()) return new(value.x * scale, value.y * scale);
        var xHigh = Math.BigMul(value.x.RawValue, scale.RawValue, out var xLow);
        _ = checked((int)xHigh);
        var xRaw = unchecked((long)(((ulong)xHigh << Fix64.FractionalBits) |
            ((ulong)xLow >> Fix64.FractionalBits)));
        var yHigh = Math.BigMul(value.y.RawValue, scale.RawValue, out var yLow);
        _ = checked((int)yHigh);
        var yRaw = unchecked((long)(((ulong)yHigh << Fix64.FractionalBits) |
            ((ulong)yLow >> Fix64.FractionalBits)));
        return new(Fix64.FromRaw(xRaw), Fix64.FromRaw(yRaw));
    }
    public static Vector2 operator *(Fix64 scale, Vector2 value) => value * scale;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Vector2 operator /(Vector2 value, Fix64 scale) => new(value.x / scale, value.y / scale);
    public static bool operator ==(Vector2 left, Vector2 right) => left.Equals(right);
    public static bool operator !=(Vector2 left, Vector2 right) => !left.Equals(right);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Fix64 Dot(Vector2 left, Vector2 right) => left.x * right.x + left.y * right.y;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Vector2 Scale(Vector2 left, Vector2 right)
    {
        if (!OperatingSystem.IsBrowser()) return new(left.x * right.x, left.y * right.y);
        var xHigh = Math.BigMul(left.x.RawValue, right.x.RawValue, out var xLow);
        _ = checked((int)xHigh);
        var xRaw = unchecked((long)(((ulong)xHigh << Fix64.FractionalBits) |
            ((ulong)xLow >> Fix64.FractionalBits)));
        var yHigh = Math.BigMul(left.y.RawValue, right.y.RawValue, out var yLow);
        _ = checked((int)yHigh);
        var yRaw = unchecked((long)(((ulong)yHigh << Fix64.FractionalBits) |
            ((ulong)yLow >> Fix64.FractionalBits)));
        return new(Fix64.FromRaw(xRaw), Fix64.FromRaw(yRaw));
    }
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
