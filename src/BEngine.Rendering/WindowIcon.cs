using Silk.NET.Core;
using Silk.NET.Windowing;

namespace BEngine.Rendering;

public static class WindowIcon
{
    private const int Size = 64;
    private const string ResourceName = "BEngine.Icons.BEngine.64.rgba";

    public static void Apply(IWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        using var stream = typeof(WindowIcon).Assembly.GetManifestResourceStream(ResourceName) ??
            throw new InvalidOperationException($"Embedded window icon '{ResourceName}' was not found.");
        var pixels = new byte[Size * Size * 4];
        stream.ReadExactly(pixels);
        RawImage[] icons = [new RawImage(Size, Size, pixels)];
        window.SetWindowIcon(icons);
    }
}
