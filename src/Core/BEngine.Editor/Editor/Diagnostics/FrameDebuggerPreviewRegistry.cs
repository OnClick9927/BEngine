using BEngine.Editor.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

/// <summary>Bridges immutable frame-debugger readbacks into the GPU IMGUI texture resolver.</summary>
internal static class FrameDebuggerPreviewRegistry
{
    private const string SourcePrefix = "__bengine_frame_debugger_preview_";
    private const string SourceSuffix = ".rgba";
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, GpuCanvasTextureData> Entries =
        new(StringComparer.Ordinal);

    internal static string Publish(
        string key,
        int width,
        int height,
        ReadOnlyMemory<byte> rgbaPixels,
        long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var source = SourceFor(key);
        var texture = new GpuCanvasTextureData(
            width,
            height,
            GraphicsTextureFormat.Rgba8Unorm,
            rgbaPixels);
        lock (Gate) Entries[source] = texture;
        return $"{source}{AssetPreview.PreviewRevisionQuery}{revision:x}";
    }

    internal static bool TryResolve(string source, out GpuCanvasTextureData texture)
    {
        texture = default;
        if (string.IsNullOrWhiteSpace(source)) return false;
        var revision = source.LastIndexOf(AssetPreview.PreviewRevisionQuery,
            StringComparison.Ordinal);
        if (revision >= 0) source = source[..revision];
        lock (Gate) return Entries.TryGetValue(source, out texture);
    }

    internal static void Remove(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var source = SourceFor(key);
        lock (Gate) Entries.Remove(source);

        // The GPU canvas canonicalizes revision-backed sources before caching them.
        // Invalidate that exact key as well so closing a debugger window releases its
        // potentially large preview texture instead of waiting for the LRU limit.
        AssetPreview.Invalidate(source);
    }

    private static string SourceFor(string key) => Path.Combine(
        AppContext.BaseDirectory,
        $"{SourcePrefix}{key}{SourceSuffix}");
}
