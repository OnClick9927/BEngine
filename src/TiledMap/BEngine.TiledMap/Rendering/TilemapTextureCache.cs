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
    private readonly Dictionary<string, IGraphicsTexture2D> _textures =
        new(StringComparer.OrdinalIgnoreCase);
    private IGraphicsTexture2D? _missing;

    internal TilemapTextureCache(IGraphicsDevice device) => _device = device;

    internal IGraphicsTexture2D Resolve(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) return Missing();
        if (_textures.TryGetValue(source, out var cached)) return cached;
        if (!TryRead(source, out var bytes) ||
            !PngTextureDecoder.TryDecode(bytes, out var width, out var height, out var pixels))
            return Missing();
        var texture = _device.CreateTexture2D(
            $"BEngine.TiledMap.{Path.GetFileName(source)}",
            new GraphicsTextureDescription(width, height, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Nearest,
                GraphicsTextureFilter.Nearest, GraphicsTextureAddressMode.ClampToEdge), pixels);
        _textures[source] = texture;
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _textures.Values) texture.Dispose();
        _textures.Clear();
        _missing?.Dispose();
        _missing = null;
    }

    private IGraphicsTexture2D Missing() => _missing ??= _device.CreateTexture2D(
        "BEngine.TiledMap.Missing",
        new GraphicsTextureDescription(2, 2, GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Nearest,
            GraphicsTextureFilter.Nearest, GraphicsTextureAddressMode.ClampToEdge), MissingPixels);

    private static bool TryRead(string source, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var path = ResolvePath(source);
            if (path is not null)
            {
                bytes = File.ReadAllBytes(path);
                return true;
            }
            bytes = Resources.Load<byte[]>(source) ?? [];
            return bytes.Length > 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? ResolvePath(string source)
    {
        if (Path.IsPathRooted(source))
        {
            var rooted = Path.GetFullPath(source);
            return File.Exists(rooted) ? rooted : null;
        }
        var normalized = source.Replace('/', Path.DirectorySeparatorChar);
        var current = Path.GetFullPath(normalized);
        if (File.Exists(current)) return current;
        var dataPath = Path.GetFullPath(Application.dataPath);
        if (normalized.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith($"Assets{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetFileName(dataPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(dataPath)?.FullName ?? Directory.GetCurrentDirectory()
                : Directory.GetCurrentDirectory();
            var assetPath = Path.GetFullPath(Path.Combine(root, normalized));
            return File.Exists(assetPath) ? assetPath : null;
        }
        var dataFile = Path.GetFullPath(Path.Combine(dataPath, normalized));
        return File.Exists(dataFile) ? dataFile : null;
    }
}
