namespace BEngine.Rendering;

/// <summary>Names and layout of sprite-renderer counters; Core has no renderer-specific fields.</summary>
public static class Rendering2DStatistics
{
    public const string BuiltQuads = "2d.geometry-built-quads";
    public const string ReusedQuads = "2d.geometry-reused-quads";
    public const string WorldGeometryBuilt = "2d.world-geometry-built";
    public const string WorldGeometryReused = "2d.world-geometry-reused";
    public const string CullingTests = "2d.culling-tests";
    public const string CullingReused = "2d.culling-reused";
    public const string CulledSprites = "2d.culled-sprites";
    public const string CoreCollectMilliseconds = "2d.core-collect-ms";
    public const string ExtensionCollectMilliseconds = "2d.extension-collect-ms";
    public const string CoreGeometryUploadCount = "2d.geometry-upload-count";
    public const string CoreGeometryUploadBytes = "2d.geometry-upload-bytes";
    public const string SortFullPassCount = "2d.sort-full-passes";
    public const string SortReusePassCount = "2d.sort-reuse-passes";
    public const string CoreDrawRangeMergeCount = "2d.merged-draw-ranges";
    public const string CoreOpaqueSolidDrawCount = "2d.opaque-solid-draws";
    public static double GetCoreCollectMilliseconds(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetDoubleValue(CoreCollectMilliseconds);
    public static double GetExtensionCollectMilliseconds(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetDoubleValue(ExtensionCollectMilliseconds);
    public static long GetCoreGeometryUploadCount(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetValue(CoreGeometryUploadCount);
    public static long GetCoreGeometryUploadBytes(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetValue(CoreGeometryUploadBytes);
    public static long GetSortFullPassCount(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetValue(SortFullPassCount);
    public static long GetSortReusePassCount(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetValue(SortReusePassCount);
    public static long GetCoreDrawRangeMergeCount(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetValue(CoreDrawRangeMergeCount);
    public static long GetCoreOpaqueSolidDrawCount(this SceneRenderStatistics statistics) => statistics.RendererCounters.GetValue(CoreOpaqueSolidDrawCount);
    internal static readonly SceneRenderCounterLayout Layout = new(
        [BuiltQuads, ReusedQuads, WorldGeometryBuilt, WorldGeometryReused, CullingTests, CullingReused, CulledSprites, CoreCollectMilliseconds, ExtensionCollectMilliseconds, CoreGeometryUploadCount, CoreGeometryUploadBytes, SortFullPassCount, SortReusePassCount, CoreDrawRangeMergeCount, CoreOpaqueSolidDrawCount],
        [CoreCollectMilliseconds, ExtensionCollectMilliseconds]);
}
