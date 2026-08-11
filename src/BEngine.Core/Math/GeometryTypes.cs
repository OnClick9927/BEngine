namespace BEngine;

public readonly struct Rect : IEquatable<Rect>
{
    public Fix64 x { get; }
    public Fix64 y { get; }
    public Fix64 width { get; }
    public Fix64 height { get; }
    public Fix64 xMin => x;
    public Fix64 yMin => y;
    public Fix64 xMax => x + width;
    public Fix64 yMax => y + height;
    public Vector2 position => new(x, y);
    public Vector2 size => new(width, height);
    public Vector2 center => new(x + width * Fix64.Half, y + height * Fix64.Half);

    public Rect(Fix64 x, Fix64 y, Fix64 width, Fix64 height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public bool Contains(Vector2 point) =>
        point.x >= xMin && point.x < xMax && point.y >= yMin && point.y < yMax;
    public bool Overlaps(Rect other) =>
        other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax;
    public bool Equals(Rect other) => x == other.x && y == other.y && width == other.width && height == other.height;
    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(x, y, width, height);
}

public readonly struct Bounds : IEquatable<Bounds>
{
    public Vector3 center { get; }
    public Vector3 size { get; }
    public Vector3 extents => size * Fix64.Half;
    public Vector3 min => center - extents;
    public Vector3 max => center + extents;

    public Bounds(Vector3 center, Vector3 size)
    {
        this.center = center;
        this.size = size;
    }

    public bool Contains(Vector3 point)
    {
        var minimum = min;
        var maximum = max;
        return point.x >= minimum.x && point.x <= maximum.x &&
               point.y >= minimum.y && point.y <= maximum.y &&
               point.z >= minimum.z && point.z <= maximum.z;
    }

    public bool Equals(Bounds other) => center == other.center && size == other.size;
    public override bool Equals(object? obj) => obj is Bounds other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(center, size);
}

public readonly struct Ray
{
    public Vector3 origin { get; }
    public Vector3 direction { get; }

    public Ray(Vector3 origin, Vector3 direction)
    {
        this.origin = origin;
        this.direction = direction.normalized;
    }

    public Vector3 GetPoint(Fix64 distance) => origin + direction * distance;
}

public readonly record struct Resolution(int width, int height, int refreshRate = 60)
{
    public override string ToString() => $"{width} x {height} @ {refreshRate}Hz";
}

public readonly struct LayerMask
{
    public int value { get; }
    public LayerMask(int value) => this.value = value;
    public static implicit operator int(LayerMask mask) => mask.value;
    public static implicit operator LayerMask(int value) => new(value);
}
