using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface ISceneRenderContributor2D : IDisposable
{
    string packageId { get; }
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder);
    bool CanRender(RenderBatch2D batch);
    void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport);
}
