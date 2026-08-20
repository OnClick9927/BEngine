namespace BEngine;

public static class Mathf
{
    public static readonly Fix64 PI = Fix64.Pi;
    public static readonly Fix64 Deg2Rad = Fix64.Deg2Rad;
    public static readonly Fix64 Rad2Deg = Fix64.Rad2Deg;
    public static readonly Fix64 Epsilon = Fix64.Epsilon;

    public static Fix64 Abs(Fix64 value) => Fix64.Abs(value);
    public static Fix64 Clamp(Fix64 value, Fix64 min, Fix64 max) => Fix64.Clamp(value, min, max);
    public static Fix64 Clamp01(Fix64 value) => Clamp(value, Fix64.Zero, Fix64.One);
    public static Fix64 Min(Fix64 left, Fix64 right) => Fix64.Min(left, right);
    public static Fix64 Max(Fix64 left, Fix64 right) => Fix64.Max(left, right);
    public static Fix64 Sqrt(Fix64 value) => Fix64.Sqrt(value);
    public static Fix64 Sin(Fix64 value) => Fix64.Sin(value);
    public static Fix64 Cos(Fix64 value) => Fix64.Cos(value);
    public static Fix64 Tan(Fix64 value) => Sin(value) / Cos(value);
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
    public static Fix64 LerpUnclamped(Fix64 from, Fix64 to, Fix64 time) => from + ((to - from) * time);
    public static Fix64 InverseLerp(Fix64 from, Fix64 to, Fix64 value) => from == to
        ? Fix64.Zero : Clamp01((value - from) / (to - from));
    public static Fix64 MoveTowards(Fix64 current, Fix64 target, Fix64 maxDelta) =>
        Abs(target - current) <= maxDelta ? target : current + (target > current ? maxDelta : -maxDelta);
    public static Fix64 Sign(Fix64 value) => value >= Fix64.Zero ? Fix64.One : -Fix64.One;
    public static int FloorToInt(Fix64 value) => (int)(value.RawValue >> Fix64.FractionalBits);
    public static int CeilToInt(Fix64 value) => FloorToInt(value) +
        ((value.RawValue & (Fix64.OneRaw - 1)) == 0 ? 0 : 1);
    public static int RoundToInt(Fix64 value) => (int)Math.Round((double)value,
        MidpointRounding.AwayFromZero);
    public static Fix64 Floor(Fix64 value) => FloorToInt(value);
    public static Fix64 Ceil(Fix64 value) => CeilToInt(value);
    public static Fix64 Round(Fix64 value) => RoundToInt(value);
    public static bool Approximately(Fix64 left, Fix64 right) => Abs(left - right) <= Fix64.Epsilon * 8;
    public static Fix64 Repeat(Fix64 value, Fix64 length) => length <= Fix64.Zero
        ? Fix64.Zero : Clamp(value - Floor(value / length) * length, Fix64.Zero, length);
    public static Fix64 PingPong(Fix64 value, Fix64 length)
    {
        value = Repeat(value, length * 2);
        return length - Abs(value - length);
    }
}
