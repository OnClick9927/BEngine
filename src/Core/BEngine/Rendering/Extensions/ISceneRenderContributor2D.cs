using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface ISceneRenderContributor2D : IDisposable
{
    string packageId { get; }
    bool rendersUi => false;
    void BeginFrame() { }
    void BeginScene(Scene scene) { }
    void BeginScene(in SceneRenderContext2D context) => BeginScene(context.Scene);
    SceneQuad2D ProcessQuad(SceneQuad2D quad, GameObject target) => quad;
    void EndFrame() { }
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder);
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter) =>
        Collect(scene, camera, width, height, submissions, submissionOrder);
    /// <summary>Uses the engine's prepared hierarchy order without rescanning the scene.</summary>
    long Collect(in SceneRenderContext2D context, ICollection<RenderSubmission2D> submissions,
        long submissionOrder) => Collect(context.Scene, context.Camera, context.Width, context.Height,
            submissions, submissionOrder, context.ObjectFilter);
    bool CanRender(RenderBatch2D batch);
    void Render(RenderBatch2D batch, RenderCamera camera, int width, int height, GraphicsRect viewport);
}
