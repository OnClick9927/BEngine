namespace BEngine.Rendering;

/// <summary>Numeric appearance of an untextured proportional bar above a circular anchor.</summary>
public readonly record struct SceneFillBarStyle2D(Fix64 Gap, Fix64 Thickness, Color Color);

// The renderer-owned sink can reuse its prepared camera bounds. Custom sinks
// retain their existing camera queries and submission callbacks.
internal interface ISceneFillBarSink2D
{
    void SubmitFillBar(Renderer2D renderer, Vector2 center, Vector2 size,
        Color color, RenderBatchKey2D batch);
}

/// <summary>Prepares and submits individual fill bars through the normal geometry sink.</summary>
public static class SceneFillBarGeometry2D
{
    public static void Submit(ISceneGeometrySink2D sink, Renderer2D renderer,
        Vector2 position, Fix64 anchorRadius, Fix64 current, Fix64 maximum,
        SceneFillBarStyle2D style, RenderBatchKey2D batch)
    {
        // Numeric evaluation deliberately precedes sink access. In particular,
        // division-by-zero and overflow remain observable even for invisible bars.
        var center = position + new Vector2(0, anchorRadius + style.Gap);
        var size = new Vector2(anchorRadius * 2 * Mathf.Clamp01(current / maximum), style.Thickness);
        if (size.x <= 0) return;
        if (sink is ISceneFillBarSink2D prepared)
        {
            prepared.SubmitFillBar(renderer, center, size, style.Color, batch);
            return;
        }
        if (!sink.Camera.IsVisible(center, 0, size, sink.Width, sink.Height)) return;
        sink.Submit(renderer, SceneQuad2D.Create(center, 0, size, style.Color, "",
            new Rect(0, 0, 1, 1), false, false, false), batch);
    }
}
