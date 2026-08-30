namespace BEngine;

public static class Random
{
    private static ulong _state = 0x9E3779B97F4A7C15UL;
    public readonly record struct State(ulong Value);
    public static State state { get => new(_state); set => _state = value.Value == 0 ? 1UL : value.Value; }
    public static Fix64 value => Fix64.FromRaw((long)(Next() >> 32));
    public static Vector2 insideUnitCircle
    {
        get
        {
            var angle = value * Fix64.TwoPi;
            var radius = Fix64.Sqrt(value);
            return new Vector2(Fix64.Cos(angle), Fix64.Sin(angle)) * radius;
        }
    }
    public static Vector2 onUnitCircle
    {
        get
        {
            var angle = value * Fix64.TwoPi;
            return new Vector2(Fix64.Cos(angle), Fix64.Sin(angle));
        }
    }
    public static void InitState(int seed) => _state = unchecked((ulong)seed) + 0x9E3779B97F4A7C15UL;
    public static int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        return minInclusive + (int)(Next() % (uint)(maxExclusive - minInclusive));
    }
    public static Fix64 Range(Fix64 minInclusive, Fix64 maxInclusive) =>
        maxInclusive <= minInclusive ? minInclusive : minInclusive + (maxInclusive - minInclusive) * value;
    private static ulong Next()
    {
        var x = _state;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        _state = x;
        return x * 2685821657736338717UL;
    }
}
