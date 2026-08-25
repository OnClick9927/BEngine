using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface ISceneRenderContributor2D : IDisposable
{
    string packageId { get; }
    bool rendersUi => false;
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder);
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter) =>
        Collect(scene, camera, width, height, submissions, submissionOrder);
    bool CanRender(RenderBatch2D batch);
    void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport);
}
