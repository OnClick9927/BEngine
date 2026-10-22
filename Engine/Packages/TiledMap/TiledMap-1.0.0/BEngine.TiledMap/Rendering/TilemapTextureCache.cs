using BEngine.Rendering.Rhi;

namespace BEngine.TiledMap;

internal sealed class TilemapTextureCache : IDisposable
{
    private static readonly byte[] MissingPixels =
    [
        44, 46, 51, 255, 220, 62, 92, 255,
        220, 62, 92, 255, 44, 46, 51, 255
    ];

    private readonly IGraphicsDevice _device;
    private readonly ITilemapTextureSource _source;
    private readonly record struct Entry(IGraphicsTexture2D Texture, bool Opaque);
    private readonly Dictionary<string, Entry> _textures =
        new(StringComparer.Ordinal);
    private long _generation = -1;
    private IGraphicsTexture2D? _missing;

    internal TilemapTextureCache(
        IGraphicsDevice device,
        ITilemapTextureSource? source = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _source = source ?? TiledMapRuntimeServices.TextureSource;
    }

    internal IGraphicsTexture2D Resolve(string source, out bool opaque)
    {
        if (_generation != BAsset.CacheGeneration)
        {
            foreach (var previous in _textures.Values) previous.Texture.Dispose();
            _textures.Clear();
            _generation = BAsset.CacheGeneration;
        }
        opaque = true; // The missing-texture checker is entirely opaque.
        if (string.IsNullOrWhiteSpace(source)) return Missing();
        if (_textures.TryGetValue(source, out var cached))
        { opaque = cached.Opaque; return cached.Texture; }
        if (!_source.TryRead(source, out var bytes)) return Missing();
        IGraphicsTexture2D texture;
        try
        {
            var label = $"BEngine.TiledMap.{_source.GetName(source)}";
            BEngine.Rendering.TextureImageData image;
            if (BEngine.Rendering.TextureImageData.IsContainer(bytes))
                image = BEngine.Rendering.TextureImageData.Deserialize(bytes);
            else
            {
                if (!BEngine.Rendering.ImageCodec.TryDecode(bytes, out var width, out var height, out var rgba))
                    return Missing();
                image = BEngine.Rendering.TextureImageData.FromOwnedRgba(width, height, rgba);
            }
            opaque = HasOpaqueAlpha(image);
            texture = BEngine.Rendering.TextureUpload.Create(_device, label, image, TextureFilterMode.Point);
        }
        catch (InvalidDataException) { return Missing(); }
        _textures[source] = new(texture, opaque);
        return texture;
    }

    private static bool HasOpaqueAlpha(BEngine.Rendering.TextureImageData image)
    {
        // The cache owns this immutable uploaded texture. Prove every stored
        // texel in every mip; alpha import hints alone are not sufficient.
        // Compressed formats conservatively retain the original blend path.
        if (image.Format != TextureCompressionFormat.Rgba32) return false;
        foreach (var level in image.MipLevels)
        {
            // Each RGBA texel contributes exactly one alpha check. Keep this
            // data-bound allowance local to the current mip's scan.
            using var alphaBudget = LoopExecutionGuard.AllowIterations(level.Length / 4);
            for (var i = 3; i < level.Length; i += 4)
                if (level[i] != byte.MaxValue) return false;
        }
        return true;
    }

    public void Dispose()
    {
        foreach (var entry in _textures.Values) entry.Texture.Dispose();
        _textures.Clear();
        _missing?.Dispose();
        _missing = null;
    }

    private IGraphicsTexture2D Missing() => _missing ??= _device.CreateTexture2D(
        "BEngine.TiledMap.Missing",
        new GraphicsTextureDescription(2, 2, GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Nearest,
            GraphicsTextureFilter.Nearest, GraphicsTextureAddressMode.ClampToEdge), MissingPixels);

}
