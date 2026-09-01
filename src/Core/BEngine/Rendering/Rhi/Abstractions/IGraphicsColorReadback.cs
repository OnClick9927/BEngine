namespace BEngine.Rendering.Rhi;

/// <summary>
/// Optional graphics-device capability for taking a single color snapshot without changing
/// the contents of the active render target.
/// </summary>
public interface IGraphicsColorReadback
{
    GraphicsColorReadbackCapabilities ColorReadbackCapabilities { get; }

    /// <summary>
    /// Requests a snapshot of <paramref name="region"/>. The region is absolute within the active
    /// color surface and always uses a top-left origin, independent of the graphics backend.
    /// </summary>
    GraphicsColorReadbackRequest RequestColorReadback(
        GraphicsRect region,
        int surfaceWidth,
        int surfaceHeight);
}

public readonly record struct GraphicsColorReadbackCapabilities(
    bool IsAvailable,
    int MaximumBytes,
    string UnavailableReason = "")
{
    public static GraphicsColorReadbackCapabilities Unavailable(string reason) =>
        new(false, 0, string.IsNullOrWhiteSpace(reason) ? "Color readback is unavailable." : reason);
}

public enum GraphicsColorReadbackStatus
{
    Pending,
    Ready,
    Unavailable,
    Failed,
    Disposed
}

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

/// <summary>
/// A one-shot readback ticket. Once ready, the returned image never changes. Dispose tickets
/// that are no longer needed so backend staging resources can be released promptly.
/// </summary>
public abstract class GraphicsColorReadbackRequest : IDisposable
{
    public abstract GraphicsRect Region { get; }
    public abstract GraphicsColorReadbackStatus Status { get; }
    public abstract string Error { get; }
    public abstract bool TryGetResult(out GraphicsColorReadbackImage? image);
    public abstract void Dispose();

    public static GraphicsColorReadbackRequest Unavailable(GraphicsRect region, string reason) =>
        new CompletedRequest(region, GraphicsColorReadbackStatus.Unavailable, null,
            string.IsNullOrWhiteSpace(reason) ? "Color readback is unavailable." : reason);

    public static GraphicsColorReadbackRequest Failed(GraphicsRect region, string error) =>
        new CompletedRequest(region, GraphicsColorReadbackStatus.Failed, null,
            string.IsNullOrWhiteSpace(error) ? "Color readback failed." : error);

    internal static GraphicsColorReadbackRequest Ready(
        GraphicsRect region,
        GraphicsColorReadbackImage image) =>
        new CompletedRequest(region, GraphicsColorReadbackStatus.Ready, image, string.Empty);

    private sealed class CompletedRequest(
        GraphicsRect region,
        GraphicsColorReadbackStatus status,
        GraphicsColorReadbackImage? image,
        string error) : GraphicsColorReadbackRequest
    {
        private bool _disposed;

        public override GraphicsRect Region => region;
        public override GraphicsColorReadbackStatus Status =>
            _disposed ? GraphicsColorReadbackStatus.Disposed : status;
        public override string Error => _disposed ? "The color readback request was disposed." : error;

        public override bool TryGetResult(out GraphicsColorReadbackImage? result)
        {
            result = !_disposed && status == GraphicsColorReadbackStatus.Ready ? image : null;
            return result is not null;
        }

        public override void Dispose() => _disposed = true;
    }
}

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
