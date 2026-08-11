namespace BEngine;

public static class Mathf
{
    public static readonly Fix64 PI = Fix64.Pi;
    public static readonly Fix64 Deg2Rad = Fix64.Deg2Rad;
    public static readonly Fix64 Rad2Deg = Fix64.Rad2Deg;

    public static Fix64 Abs(Fix64 value) => Fix64.Abs(value);
    public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max) => Fix64.Clamp(value, min, max);
    public static Fix64 Clamp01(Fix64 value) => Clamp(value, Fix64.Zero, Fix64.One);
    public static Fix64 Min(Fix64 left, Fix64 right) => Fix64.Min(left, right);
    public static Fix64 Max(Fix64 left, Fix64 right) => Fix64.Max(left, right);
    public static Fix64 Sqrt(Fix64 value) => Fix64.Sqrt(value);
    public static Fix64 Sin(Fix64 value) => Fix64.Sin(value);
    public static Fix64 Cos(Fix64 value) => Fix64.Cos(value);
    public static Fix64 Atan2(Fix64 y, Fix64 x)
    {
        if (x == 0) return y > 0 ? Fix64.HalfPi : y < 0 ? -Fix64.HalfPi : Fix64.Zero;
        var ax = Abs(x);
        var ay = Abs(y);
        var ratio = Min(ax, ay) / Max(ax, ay);
        var angle = ratio * (Fix64.Pi / 4 + Fix64.Parse("0.273") * (Fix64.One - ratio));
        if (ay > ax) angle = Fix64.HalfPi - angle;
        if (x < 0) angle = Fix64.Pi - angle;
        return y < 0 ? -angle : angle;
    }
    public static Fix64 Lerp(Fix64 from, Fix64 to, Fix64 time) => from + ((to - from) * Clamp01(time));
}
