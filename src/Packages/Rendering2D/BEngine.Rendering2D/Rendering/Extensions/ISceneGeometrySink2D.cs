namespace BEngine.Rendering;

/// <summary>Shared quad submission API for optional renderer components. Valid only during
/// the current CollectGeometry callback; do not retain the sink between callbacks.</summary>
public interface ISceneGeometrySink2D
{
    RenderCamera Camera { get; }
    int Width { get; }
    int Height { get; }
    void Submit(Renderer2D renderer, SceneQuad2D quad, RenderBatchKey batchKey);
}

