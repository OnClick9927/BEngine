using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

/// <summary>Resolves image icons from registered Editor and Resources roots without platform UI APIs.</summary>
public sealed class FileGpuCanvasResourceResolver : IGpuCanvasResourceResolver
{
    public static FileGpuCanvasResourceResolver Shared { get; } = new();

    public bool TryResolveTexture(string source, out GpuCanvasTextureData texture)
    {
        texture = default;
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = ResolvePath(source);
            if (path is null || !File.Exists(path)) return false;
            if (!BEngine.Rendering.ImageCodec.TryDecode(File.ReadAllBytes(path),
                    out var width, out var height, out var rgba)) return false;
            texture = new GpuCanvasTextureData(width, height, GraphicsTextureFormat.Rgba8Unorm, rgba);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ResolvePath(string source)
    {
        var revision = source.LastIndexOf(AssetPreview.PreviewRevisionQuery, StringComparison.Ordinal);
        if (revision >= 0) source = source[..revision];
        if (Path.IsPathRooted(source)) return Path.GetFullPath(source);
        return ResourceLoader.Resolve(source, EditorResource.DirectoryName) ??
               ResourceLoader.Resolve(source, "Resources");
    }

}
