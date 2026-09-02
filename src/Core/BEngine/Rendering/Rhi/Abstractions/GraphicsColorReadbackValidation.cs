namespace BEngine.Rendering.Rhi;

internal static class GraphicsColorReadbackValidation
{
    internal const int DefaultMaximumBytes = 64 * 1024 * 1024;

    internal static int ValidateRegion(
        GraphicsRect region,
        int surfaceWidth,
        int surfaceHeight,
        int maximumBytes)
    {
        region.Validate();
        if (surfaceWidth <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceWidth));
        if (surfaceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(surfaceHeight));
        if (region.X < 0 || region.Y < 0 ||
            (long)region.X + region.Width > surfaceWidth ||
            (long)region.Y + region.Height > surfaceHeight)
            throw new ArgumentOutOfRangeException(nameof(region),
                "The color readback region must be inside the active color surface.");
        var bytes = checked(region.Width * region.Height * 4);
        if (bytes > maximumBytes)
            throw new ArgumentOutOfRangeException(nameof(region),
                $"The color readback requires {bytes} bytes; the maximum is {maximumBytes} bytes.");
        return bytes;
    }
}
