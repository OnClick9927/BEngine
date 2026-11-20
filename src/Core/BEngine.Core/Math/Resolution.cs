namespace BEngine;

public readonly record struct Resolution(int width, int height, int refreshRate = 60)
{
    public override string ToString() => $"{width} x {height} @ {refreshRate}Hz";
}
