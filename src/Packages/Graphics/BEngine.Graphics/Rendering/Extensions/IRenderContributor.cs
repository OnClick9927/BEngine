using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

/// <summary>A device-owned contributor to an ordered stream of drawing commands.</summary>
public interface IRenderContributor : IDisposable
{
    string packageId { get; }
    bool rendersUi => false;
    void BeginFrame() { }
    void EndFrame() { }
    bool CanRender(RenderBatch batch);
}

/// <summary>Drawing extension that does not depend on a scene renderer or world projection.</summary>
public interface IDrawContributor : IRenderContributor
{
    void BeginScene(in RenderCollectionContext context) { }
    long Collect(in RenderCollectionContext context, ICollection<RenderSubmission> submissions, long submissionOrder);
    void Render(RenderBatch batch, int width, int height, GraphicsRect viewport);
}

/// <summary>Source selection and target dimensions, without a concrete camera or geometry policy.</summary>
public readonly record struct RenderCollectionContext(
    Scene Scene, int Width, int Height, ulong CullingMask, Predicate<GameObject>? ObjectFilter = null)
{
    public bool ContainsLayer(ulong layer) => RenderLayerMask.Contains(CullingMask, layer);
}

public static class RenderLayerMask
{
    public static bool Contains(ulong mask, ulong layer)
    {
        var offset = unchecked(layer - (ulong)SortingLayer.MinimumIndex);
        return offset <= (ulong)(SortingLayer.MaximumIndex - SortingLayer.MinimumIndex) &&
               (mask & (1UL << (int)offset)) != 0;
    }
}
