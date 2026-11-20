using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public sealed partial class PortableSceneRenderer
{
    /// <summary>Merges adjacent Core ranges using the same actual pipeline/texture. Never reorders draws.
    /// Individual batches remain available when pass timing or debug markers are enabled.</summary>
    public bool coreDrawRangeMergingEnabled { get; set; } = true;
    private long _mergedCoreRanges;

    private bool TryDrawMergedCoreRanges(ref int index, int batchCount, int width, int height)
    {
        if (!coreDrawRangeMergingEnabled || GraphicsFrameTimingService.IsPassTimingEnabled ||
            _debugAnnotations?.DebugMarkersEnabled == true || _batchContributors[index] is not null ||
            _batches[index].BatchKey.MaskGroup != 0) return false;
        var range = _coreBatchRanges[index];
        if (range.VertexCount == 0) return false;
        var last = index;
        while (last + 1 < batchCount)
        {
            var nextIndex = last + 1;
            if (_batchContributors[nextIndex] is not null || _batches[nextIndex].BatchKey.MaskGroup != 0) break;
            var next = _coreBatchRanges[nextIndex];
            if (next.VertexCount == 0 || next.Textured != range.Textured ||
                next.FirstVertex != range.FirstVertex + range.VertexCount ||
                range.Textured && !string.Equals(next.Texture, range.Texture, StringComparison.Ordinal)) break;
            // Do not split a draw merely to disable blending. A merged range
            // qualifies only when every existing component range is opaque.
            range = range with { VertexCount = checked(range.VertexCount + next.VertexCount), Opaque = range.Opaque && next.Opaque };
            last = nextIndex;
        }
        if (last == index) return false;
        DrawPreparedBatch(range, width, height);
        _mergedCoreRanges += last - index;
        index = last;
        return true;
    }
}
