using System.Collections;
using System.Runtime.CompilerServices;

namespace BEngine.UIElements;

public readonly record struct UIColor(byte R, byte G, byte B, byte A = 255)
{
    public static UIColor FromRgb(byte red, byte green, byte blue) => new(red, green, blue);
    public static UIColor FromArgb(byte alpha, byte red, byte green, byte blue) => new(red, green, blue, alpha);
    public static UIColor Clear => new(0, 0, 0, 0);
}
