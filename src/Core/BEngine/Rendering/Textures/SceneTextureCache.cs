using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

internal sealed class SceneTextureCache : IDisposable
{
    private static readonly byte[] MissingPixels =
    [
        44, 46, 51, 255, 220, 62, 92, 255,
        220, 62, 92, 255, 44, 46, 51, 255
    ];

    private readonly IGraphicsDevice _device;
    private readonly Dictionary<string, CacheEntry> _textures = new(StringComparer.OrdinalIgnoreCase);
    private IGraphicsTexture2D? _missing;

    internal SceneTextureCache(IGraphicsDevice device) => _device = device;

    internal IGraphicsTexture2D Resolve(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.StartsWith("missing:", StringComparison.Ordinal))
            return Missing();
        if (TryResolveRuntimeTexture(source, out var runtimeTexture)) return runtimeTexture;
        var path = ResolvePath(source);
        var isGuidSubAsset = path is null && source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                             source.Contains("#subasset=", StringComparison.OrdinalIgnoreCase);
        if (!isGuidSubAsset && !Texture.IsSupportedSourcePath(path ?? source)) return Missing();
        var stamp = SourceStamp.Read(path);
        if (_textures.TryGetValue(source, out var cached) && cached.Stamp == stamp)
            return cached.Texture;
        if (!TryRead(source, path, out var bytes) ||
            !PngImageCodec.TryDecode(bytes, out var width, out var height, out var pixels))
            return Missing();
        var (filterMode, wrapMode) = path is null
            ? (TextureFilterMode.Bilinear, TextureWrapMode.Clamp)
            : BAssetReferenceLoader.ReadTextureSamplingSettings(path);
        var filter = filterMode == TextureFilterMode.Point
            ? GraphicsTextureFilter.Nearest
            : GraphicsTextureFilter.Linear;
        var addressMode = wrapMode switch
        {
            TextureWrapMode.Clamp => GraphicsTextureAddressMode.ClampToEdge,
            TextureWrapMode.Repeat => GraphicsTextureAddressMode.Repeat,
            TextureWrapMode.Mirror => GraphicsTextureAddressMode.MirroredRepeat,
            _ => GraphicsTextureAddressMode.ClampToEdge
        };
        var texture = _device.CreateTexture2D(
            $"BEngine.Scene2D.{Path.GetFileName(source)}",
            new GraphicsTextureDescription(width, height, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureUsage.Sampled, filter, filter, addressMode), pixels);
        if (_textures.Remove(source, out var previous)) previous.Texture.Dispose();
        _textures[source] = new CacheEntry(stamp, texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var entry in _textures.Values) entry.Texture.Dispose();
        _textures.Clear();
        _missing?.Dispose();
        _missing = null;
    }

    private IGraphicsTexture2D Missing() => _missing ??= _device.CreateTexture2D(
        "BEngine.Scene2D.MissingTexture",
        new GraphicsTextureDescription(2, 2, GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.Sampled, GraphicsTextureFilter.Nearest,
            GraphicsTextureFilter.Nearest, GraphicsTextureAddressMode.ClampToEdge), MissingPixels);

    private bool TryResolveRuntimeTexture(string source, out IGraphicsTexture2D texture)
    {
        texture = null!;
        const string prefix = "memory-texture:";
        if (!source.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(source.AsSpan(prefix.Length), out var instanceId) ||
            BObject.FindObjectFromInstanceID(instanceId) is not Texture runtime) return false;
        if (runtime is RenderTexture renderTexture && renderTexture.TryGetSampleTexture(_device, out texture))
            return true;
        if (!runtime.TryGetRuntimeRgba(out var pixels)) return false;
        var stamp = new SourceStamp(DateTime.MinValue, DateTime.MinValue, runtime.PixelVersion);
        if (_textures.TryGetValue(source, out var cached) && cached.Stamp == stamp)
        {
            texture = cached.Texture;
            return true;
        }
        texture = _device.CreateTexture2D($"BEngine.Scene2D.Runtime.{instanceId}",
            new GraphicsTextureDescription(runtime.width, runtime.height, GraphicsTextureFormat.Rgba8Unorm,
                GraphicsTextureUsage.Sampled,
                runtime.filterMode == TextureFilterMode.Point
                    ? GraphicsTextureFilter.Nearest
                    : GraphicsTextureFilter.Linear,
                runtime.filterMode == TextureFilterMode.Point
                    ? GraphicsTextureFilter.Nearest
                    : GraphicsTextureFilter.Linear,
                runtime.wrapMode switch
                {
                    TextureWrapMode.Repeat => GraphicsTextureAddressMode.Repeat,
                    TextureWrapMode.Mirror => GraphicsTextureAddressMode.MirroredRepeat,
                    _ => GraphicsTextureAddressMode.ClampToEdge
                }), pixels);
        if (_textures.Remove(source, out var previous)) previous.Texture.Dispose();
        _textures[source] = new CacheEntry(stamp, texture);
        return true;
    }

    private static bool TryRead(string source, string? path, out byte[] bytes)
    {
        bytes = [];
        try
        {
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
        try
        {
            if (source.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) &&
                BAsset.Load<Texture>(source) is { } imported)
            {
                var importedPath = string.IsNullOrWhiteSpace(imported.artifactPath)
                    ? imported.sourcePath
                    : imported.artifactPath;
                if (!string.IsNullOrWhiteSpace(importedPath) && File.Exists(importedPath))
                    return Path.GetFullPath(importedPath);
            }
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private readonly record struct SourceStamp(
        DateTime SourceWriteTimeUtc,
        DateTime MetaWriteTimeUtc,
        int RuntimeVersion = 0)
    {
        internal static SourceStamp Read(string? path)
        {
            if (path is null) return default;
            var metaPath = path + ".meta";
            return new SourceStamp(
                File.GetLastWriteTimeUtc(path),
                File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath) : DateTime.MinValue);
        }
    }

    private sealed record CacheEntry(SourceStamp Stamp, IGraphicsTexture2D Texture);
}
