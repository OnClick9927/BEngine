namespace BEngine.Rendering;

/// <summary>A world quad and its material grouping, suitable for a reusable submission list.</summary>
public readonly record struct SceneQuadSubmission2D(SceneQuad2D Quad, RenderBatchKey2D Key);

// Only the renderer-owned sink participates. Public/custom sinks retain their
// existing callbacks, live camera reads and List enumeration behavior.
internal interface ISceneQuadBatchSink2D
{
    void SubmitQuads(Renderer2D renderer, List<SceneQuadSubmission2D> quads, bool snapCenters);
}

/// <summary>Submits a prepared world geometry list through the engine's normal rendering pipeline.</summary>
public static class SceneGeometryBatch
{
    public static void SubmitQuads(ISceneGeometrySink2D sink, Renderer2D renderer,
        List<SceneQuadSubmission2D> quads, bool snapCenters = false)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(quads);
        if (sink is ISceneQuadBatchSink2D batch)
        {
            batch.SubmitQuads(renderer, quads, snapCenters);
            return;
        }
        // Keep List enumeration's mutation checks, and read the live camera for
        // each item just as individual submissions do. Optional renderers may
        // call back into gameplay from Submit.
        foreach (var item in quads)
        {
            var quad = item.Quad;
            if (snapCenters)
            {
                var camera = sink.Camera;
                // Keep the live per-item camera query, including custom sink
                // callbacks, but avoid copying this large value for identity snapping.
                if (camera.Policy is not null) quad = quad with { Center = camera.SnapWorldPoint(quad.Center) };
            }
            sink.Submit(renderer, quad, item.Key);
        }
    }
}
