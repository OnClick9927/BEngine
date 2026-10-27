using BEngine.UIElements;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public sealed partial class UIElementsRenderer
{
    // A changing label replaces its own latest geometry instead of adding a
    // cache entry for every animation position, color or number. Other labels
    // remain reusable even when their document's command list is rebuilt.
    private const int MaximumSdfGeometryEntries = 512;
    private const int MaximumSdfCommandVertexFloats = 64 * 1024;
    private const long MaximumSdfGeometryBytes = 8 * 1024 * 1024;
    private readonly BoundedLruCache<VisualElement, SdfCommandGeometry> _sdfCommandGeometry;
    private long _sdfGeometryBytes;
    private int _textGeometryCacheHits, _textGeometryRebuiltGlyphs;

    // Retain an uncached path for byte/state-equivalence regression.
    internal bool EnableSdfGeometryCache { get; set; } = true;
    internal int CachedSdfGeometryCount => _sdfCommandGeometry.Count;
    internal long CachedSdfGeometryBytes => _sdfGeometryBytes;

    private readonly record struct SdfGeometryKey(UIRenderCommand Command, SdfFontAsset Font,
        long AssetGeneration, Fix64 OutlineWidth, UIColor? OutlineColor, Fix64 EffectiveFontSize);

    private sealed class SdfCommandGeometry
    {
        internal SdfGeometryKey Key;
        internal bool Valid;
        internal UIElementRect Bounds;
        internal float? OutlineThreshold;
        internal readonly List<float> Outline = [], Fill = [];
        internal long RetainedBytes => ((long)Outline.Capacity + Fill.Capacity) * sizeof(float);
    }

    private void ReleaseSdfGeometry(SdfCommandGeometry geometry) => _sdfGeometryBytes -= geometry.RetainedBytes;

    private void ClearSdfGeometry()
    {
        _sdfCommandGeometry.Clear();
        _sdfGeometryBytes = 0;
    }

    private void TrimSdfGeometry()
    {
        while (_sdfCommandGeometry.Count > MaximumSdfGeometryEntries || _sdfGeometryBytes > MaximumSdfGeometryBytes)
            _sdfCommandGeometry.Trim(_sdfCommandGeometry.Count - 1, long.MaxValue);
    }

    private void ReplaySdfGeometry(List<RenderBatch> batches, UIRenderCommand command,
        IGraphicsTexture2D texture, SdfCommandGeometry geometry)
    {
        // Replay the original outline/fill calls through the live batch search.
        // Never retain a scene batch, texture, upload offset or target state.
        if (geometry.OutlineThreshold is { } threshold)
            AppendSdfGeometry(GetBatch(batches, command.ClipRect, texture, geometry.Bounds, threshold).Vertices, geometry.Outline);
        if (geometry.Fill.Count > 0)
            AppendSdfGeometry(GetBatch(batches, command.ClipRect, texture, geometry.Bounds, .5f).Vertices, geometry.Fill);
    }

    private static void AppendSdfGeometry(List<float> output, List<float> vertices)
    {
        var required = checked(output.Count + vertices.Count);
        // AddRange grows by a whole label instead of single floats. Avoid a
        // growth step crossing the batch pool's per-list retention limit when
        // the active vertices still fit, otherwise a warm HUD reallocates it
        // every frame despite retaining less data than the existing budget.
        if (output.Capacity < required && required <= MaximumPooledBatchVertexFloats &&
            (long)output.Capacity * 2 > MaximumPooledBatchVertexFloats)
            output.Capacity = MaximumPooledBatchVertexFloats;
        output.AddRange(vertices);
    }

    private void PrepareSdfGeometry(SdfCommandGeometry geometry, SdfGeometryKey key, SdfTextLayout layout,
        UIRenderCommand command, float top, UIElementRect bounds, float? outlineThreshold)
    {
        geometry.Valid = false;
        var retained = geometry.RetainedBytes;
        try
        {
            geometry.Outline.Clear(); geometry.Fill.Clear();
            geometry.Bounds = bounds; geometry.OutlineThreshold = outlineThreshold;
            if (outlineThreshold.HasValue)
                AddGlyphs(geometry.Outline, layout, command, top, key.OutlineColor!.Value);
            AddGlyphs(geometry.Fill, layout, command, top, command.Color);
            geometry.Key = key; geometry.Valid = true;
        }
        finally
        {
            _sdfGeometryBytes += geometry.RetainedBytes - retained;
            TrimSdfGeometry();
        }
    }
}
