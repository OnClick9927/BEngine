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
