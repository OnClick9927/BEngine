using Silk.NET.Core;
using Silk.NET.Windowing;

namespace BEngine.Rendering;

public static class WindowIcon
{
    private const int Size = 64;
    private const string ResourcePath = "Icons/BEngine.64.rgba";

    public static void Apply(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var pixels = Resources.Load<byte[]>(ResourcePath) ??
            throw new FileNotFoundException($"Runtime window icon '{ResourcePath}' was not found in Resources.");
        if (pixels.Length != Size * Size * 4)
            throw new InvalidDataException($"Runtime window icon '{ResourcePath}' must contain " +
                                           $"{Size * Size * 4} RGBA bytes, but contains {pixels.Length}.");
        RawImage[] icons = [new RawImage(Size, Size, pixels)];
        window.SetWindowIcon(icons);
    }
}
