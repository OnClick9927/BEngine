namespace BEngine.Editor;

internal static class EditorColorMath
{
    internal static void RgbToHsv(Color color, out Fix64 hue, out Fix64 saturation, out Fix64 value)
    {
        var red = Fix64.Max(0, color.r);
        var green = Fix64.Max(0, color.g);
        var blue = Fix64.Max(0, color.b);
        var maximum = Fix64.Max(red, Fix64.Max(green, blue));
        var minimum = Fix64.Min(red, Fix64.Min(green, blue));
        var delta = maximum - minimum;
        value = maximum;
        saturation = maximum <= 0 ? Fix64.Zero : delta / maximum;
        if (delta <= 0)
        {
            hue = Fix64.Zero;
            return;
        }

        hue = maximum == red
            ? ((green - blue) / delta) % (Fix64)6
            : maximum == green
                ? (blue - red) / delta + 2
                : (red - green) / delta + 4;
        hue /= 6;
        if (hue < 0) hue += Fix64.One;
    }

    internal static Color HsvToRgb(Fix64 hue, Fix64 saturation, Fix64 value, Fix64 alpha)
    {
        hue = Wrap01(hue);
        saturation = Fix64.Clamp(saturation, 0, 1);
        value = Fix64.Max(0, value);
        var scaled = hue * 6;
        var sector = Math.Clamp((int)(double)scaled, 0, 5);
        var fraction = scaled - sector;
        var p = value * (Fix64.One - saturation);
        var q = value * (Fix64.One - saturation * fraction);
        var t = value * (Fix64.One - saturation * (Fix64.One - fraction));
        return sector switch
        {
            0 => new Color(value, t, p, alpha),
            1 => new Color(q, value, p, alpha),
            2 => new Color(p, value, t, alpha),
            3 => new Color(p, q, value, alpha),
            4 => new Color(t, p, value, alpha),
            _ => new Color(value, p, q, alpha)
        };
    }

    internal static Color OpaquePreview(Color color)
    {
        var peak = Fix64.Max(Fix64.One, Fix64.Max(color.r, Fix64.Max(color.g, color.b)));
        return new Color(Fix64.Clamp(color.r / peak, 0, 1), Fix64.Clamp(color.g / peak, 0, 1),
            Fix64.Clamp(color.b / peak, 0, 1), 1);
    }

    internal static Color Lerp(Color from, Color to, Fix64 time)
    {
        time = Fix64.Clamp(time, 0, 1);
        return new Color(Mathf.Lerp(from.r, to.r, time), Mathf.Lerp(from.g, to.g, time),
            Mathf.Lerp(from.b, to.b, time), Mathf.Lerp(from.a, to.a, time));
    }

    private static Fix64 Wrap01(Fix64 value)
    {
        value %= Fix64.One;
        return value < 0 ? value + Fix64.One : value;
    }
}
