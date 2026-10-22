namespace BEngine.Rendering.Rhi;

/// <summary>An immutable top-left-origin RGBA8 image.</summary>
public sealed class GraphicsColorReadbackImage
{
    private readonly byte[] _pixels;

    public GraphicsColorReadbackImage(int width, int height, ReadOnlySpan<byte> rgbaPixels)
        : this(width, height, rgbaPixels.ToArray(), true)
    {
    }

    private GraphicsColorReadbackImage(int width, int height, byte[] ownedRgbaPixels, bool _)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(ownedRgbaPixels);
        var expected = checked(width * height * 4);
        if (ownedRgbaPixels.Length != expected)
            throw new ArgumentException($"RGBA8 image requires {expected} bytes, got {ownedRgbaPixels.Length}.",
                nameof(ownedRgbaPixels));
        Width = width;
        Height = height;
        _pixels = ownedRgbaPixels;
    }

    internal static GraphicsColorReadbackImage FromOwnedRgba8(
        int width,
        int height,
        byte[] ownedRgbaPixels) =>
        new(width, height, ownedRgbaPixels, true);

    public int Width { get; }
    public int Height { get; }
    public GraphicsTextureFormat Format => GraphicsTextureFormat.Rgba8Unorm;
    public ReadOnlyMemory<byte> Pixels => _pixels;

    public void CopyPixelsTo(Span<byte> destination)
    {
        if (destination.Length < _pixels.Length)
            throw new ArgumentException("The destination is smaller than the image.", nameof(destination));
        _pixels.CopyTo(destination);
    }
}
