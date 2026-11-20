namespace BEngine;

public readonly struct Color : IEquatable<Color>
{
    public static readonly Color white = new(1, 1, 1, 1);
    public static readonly Color black = new(0, 0, 0, 1);
    public static readonly Color red = new(1, 0, 0, 1);
    public static readonly Color green = new(0, 1, 0, 1);
    public static readonly Color blue = new(0, 0, 1, 1);

    public Fix64 r { get; }
    public Fix64 g { get; }
    public Fix64 b { get; }
    public Fix64 a { get; }

    public Color(Fix64 r, Fix64 g, Fix64 b)
        : this(r, g, b, Fix64.One)
    {
    }

    public Color(Fix64 r, Fix64 g, Fix64 b, Fix64 a)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(r, g, b, a);
    public override string ToString() => $"({r}, {g}, {b}, {a})";
    public static Color operator *(Color left, Color right) => new(
        left.r * right.r, left.g * right.g, left.b * right.b, left.a * right.a);
}
