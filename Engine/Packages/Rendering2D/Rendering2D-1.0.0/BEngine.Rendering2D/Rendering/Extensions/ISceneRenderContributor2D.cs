using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public interface ISceneRenderContributor2D : IRenderContributor
{
    /// <summary>
    /// Whether this contributor participates in ProcessQuad. Return constant false
    /// only when it performs no quad processing. The default preserves custom
    /// contributors, including existing implementations overriding ProcessQuad.
    /// </summary>
    bool processesQuads => true;
    void BeginScene(Scene scene) { }
    void BeginScene(in SceneRenderContext2D context) => BeginScene(context.Scene);
    SceneQuad2D ProcessQuad(SceneQuad2D quad, GameObject target) => quad;
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission> submissions, long submissionOrder);
    long Collect(Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission> submissions, long submissionOrder,
        Predicate<GameObject>? objectFilter) =>
        Collect(scene, camera, width, height, submissions, submissionOrder);
    /// <summary>Uses the engine's prepared hierarchy order without rescanning the scene.</summary>
    long Collect(in SceneRenderContext2D context, ICollection<RenderSubmission> submissions,
        long submissionOrder) => Collect(context.Scene, context.Camera, context.Width, context.Height,
            submissions, submissionOrder, context.ObjectFilter);
    void Render(RenderBatch batch, RenderCamera camera, int width, int height, GraphicsRect viewport);
}

/// <summary>Built-in friend-package fast path. The reference must point to a renderer-owned local snapshot.</summary>
internal interface ISceneQuadReferenceProcessor2D
{
    void ProcessQuadReferenced(scoped ref SceneQuad2D quad, GameObject target);
}
