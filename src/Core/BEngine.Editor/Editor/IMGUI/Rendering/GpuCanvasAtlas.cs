using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

internal sealed class GpuCanvasAtlas : IDisposable
{
    private const int Size = 2048;
    private const int Padding = 1;
    private readonly byte[] _pixels = new byte[Size * Size * 4];
    private readonly Dictionary<string, GpuCanvasAtlasRegion> _regions = new(StringComparer.Ordinal);
    private int _cursorX = Padding;
    private int _cursorY = Padding;
    private int _rowHeight;
    private bool _dirty;

    public bool CapacityExceeded { get; private set; }

    public GpuCanvasAtlas(IGraphicsDevice device)
    {
        var whiteOffset = PixelOffset(_cursorX, _cursorY);
        _pixels.AsSpan(whiteOffset, 4).Fill(byte.MaxValue);
        WhiteRegion = new GpuCanvasAtlasRegion(
            (_cursorX + 0.5f) / Size,
            (_cursorY + 0.5f) / Size,
            (_cursorX + 0.5f) / Size,
            (_cursorY + 0.5f) / Size);
        _cursorX += 1 + Padding;
        _rowHeight = 1;
        Texture = device.CreateTexture2D("BEngine.GpuCanvas.Atlas",
            new GraphicsTextureDescription(Size, Size, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Linear, GraphicsTextureFilter.Linear,
                GraphicsTextureAddressMode.ClampToEdge), _pixels);
    }

    public IGraphicsTexture2D Texture { get; }
    public GpuCanvasAtlasRegion WhiteRegion { get; }

    public bool TryGet(string key, out GpuCanvasAtlasRegion region) => _regions.TryGetValue(key, out region);

    public bool TryGetOrAdd(string key, GpuCanvasTextureData data, out GpuCanvasAtlasRegion region)
    {
        if (_regions.TryGetValue(key, out region)) return true;
        data.Validate();
        if (data.Width + Padding * 2 > Size || data.Height + Padding * 2 > Size)
        {
            CapacityExceeded = true;
            return false;
        }
        if (_cursorX + data.Width + Padding > Size)
        {
            _cursorX = Padding;
            _cursorY += _rowHeight + Padding * 2;
            _rowHeight = 0;
        }
        if (_cursorY + data.Height + Padding > Size)
        {
            CapacityExceeded = true;
            return false;
        }

        CopyPixels(data, _cursorX, _cursorY);
        ExtrudeBorder(_cursorX, _cursorY, data.Width, data.Height);
        region = new GpuCanvasAtlasRegion(
            (_cursorX + 0.5f) / Size,
            (_cursorY + 0.5f) / Size,
            (_cursorX + data.Width - 0.5f) / Size,
            (_cursorY + data.Height - 0.5f) / Size);
        _regions.Add(key, region);
        _cursorX += data.Width + Padding * 2;
        _rowHeight = Math.Max(_rowHeight, data.Height);
        _dirty = true;
        return true;
    }

    public bool Flush()
    {
        if (!_dirty) return false;
        Texture.Update(_pixels);
        _dirty = false;
        return true;
    }

    public void Reset()
    {
        Array.Clear(_pixels);
        _regions.Clear();
        _cursorX = Padding;
        _cursorY = Padding;
        _rowHeight = 1;
        CapacityExceeded = false;
        var whiteOffset = PixelOffset(_cursorX, _cursorY);
        _pixels.AsSpan(whiteOffset, 4).Fill(byte.MaxValue);
        _cursorX += 1 + Padding;
        _dirty = true;
    }

    public void Dispose() => Texture.Dispose();

    private void CopyPixels(GpuCanvasTextureData data, int destinationX, int destinationY)
    {
        var source = data.Pixels.Span;
        if (data.Format == GraphicsTextureFormat.Rgba8Unorm)
        {
            var sourceStride = data.Width * 4;
            for (var row = 0; row < data.Height; row++)
                source.Slice(row * sourceStride, sourceStride)
                    .CopyTo(_pixels.AsSpan(PixelOffset(destinationX, destinationY + row), sourceStride));
            return;
        }

        for (var row = 0; row < data.Height; row++)
        for (var column = 0; column < data.Width; column++)
        {
            var alpha = source[row * data.Width + column];
            var offset = PixelOffset(destinationX + column, destinationY + row);
            _pixels[offset] = byte.MaxValue;
            _pixels[offset + 1] = byte.MaxValue;
            _pixels[offset + 2] = byte.MaxValue;
            _pixels[offset + 3] = alpha;
        }
    }

    private void ExtrudeBorder(int x, int y, int width, int height)
    {
        for (var column = 0; column < width; column++)
        {
            CopyPixel(x + column, y, x + column, y - 1);
            CopyPixel(x + column, y + height - 1, x + column, y + height);
        }
        for (var row = -1; row <= height; row++)
        {
            CopyPixel(x, Math.Clamp(y + row, y, y + height - 1), x - 1, y + row);
            CopyPixel(x + width - 1, Math.Clamp(y + row, y, y + height - 1), x + width, y + row);
        }
    }

    private void CopyPixel(int sourceX, int sourceY, int destinationX, int destinationY) =>
        _pixels.AsSpan(PixelOffset(sourceX, sourceY), 4)
            .CopyTo(_pixels.AsSpan(PixelOffset(destinationX, destinationY), 4));

    private static int PixelOffset(int x, int y) => (y * Size + x) * 4;
}
