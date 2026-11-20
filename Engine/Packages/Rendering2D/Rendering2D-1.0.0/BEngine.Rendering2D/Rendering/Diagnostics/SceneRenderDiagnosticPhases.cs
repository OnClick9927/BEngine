namespace BEngine.Rendering;

/// <summary>CPU phases implemented by the 2D scene renderer.</summary>
public static class SceneRenderDiagnosticPhases
{
    public static readonly RenderDiagnosticPhase SimpleSprites = new("Rendering2D.SimpleSprites");
    public static readonly RenderDiagnosticPhase NonSpriteRenderers = new("Rendering2D.NonSpriteRenderers");
    public static readonly RenderDiagnosticPhase Sort = new("Rendering2D.Sort");
    public static readonly RenderDiagnosticPhase BuildContiguous = new("Rendering2D.BuildContiguous");
}
