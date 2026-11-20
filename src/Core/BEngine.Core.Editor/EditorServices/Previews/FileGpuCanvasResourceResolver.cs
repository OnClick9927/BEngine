using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Rendering;

/// <summary>Resolves shared editor images from registered Editor and Resources roots.</summary>
public sealed class FileGpuCanvasResourceResolver : IGpuCanvasResourceResolver
{
    public static FileGpuCanvasResourceResolver Shared { get; } = new();
    private static readonly ResourceFolder RuntimeResources = new("Resources");

    public bool TryResolveTexture(string source, out EditorTextureData texture)
    {
        texture = default;
        if (string.IsNullOrWhiteSpace(source)) return false;
        try
        {
            var path = ResolvePath(source);
            if (path is null || !File.Exists(path)) return false;
            if (!BEngine.Platform.EncodedImages.TryDecode(File.ReadAllBytes(path),
                    out var width, out var height, out var rgba)) return false;
            texture = new EditorTextureData(width, height, GraphicsTextureFormat.Rgba8Unorm, rgba);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ResolvePath(string source)
    {
        var revision = source.LastIndexOf(EditorAssetPreviewSources.PreviewRevisionQuery, StringComparison.Ordinal);
        if (revision >= 0) source = source[..revision];
        if (Path.IsPathRooted(source)) return Path.GetFullPath(source);
        return EditorResource.FindPath(source) ?? RuntimeResources.FindPath(source);
    }

}
