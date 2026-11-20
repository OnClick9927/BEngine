namespace BEngine.Rendering;

/// <summary>CPU phases implemented by the UI renderer.</summary>
public static class UIRenderDiagnosticPhases
{
    public static readonly RenderDiagnosticPhase Render = new("UI.Render");
    public static readonly RenderDiagnosticPhase BuildBatches = new("UI.BuildBatches");
    public static readonly RenderDiagnosticPhase WebSdfClip = new("UI.WebSdfClip");
    public static readonly RenderDiagnosticPhase WebSolidClip = new("UI.WebSolidClip");
    public static readonly RenderDiagnosticPhase Upload = new("UI.Upload");
    public static readonly RenderDiagnosticPhase Draw = new("UI.Draw");
    public static readonly RenderDiagnosticPhase ImageResolve = new("UI.ImageResolve");
    public static readonly RenderDiagnosticPhase ImageUpload = new("UI.ImageUpload");
    public static readonly RenderDiagnosticPhase TextRaster = new("UI.TextRaster");
    public static readonly RenderDiagnosticPhase TextUpload = new("UI.TextUpload");
    public static readonly RenderDiagnosticPhase LayoutAndCommands = new("UI.LayoutAndCommands");
    public static readonly RenderDiagnosticPhase Submit = new("UI.Submit");
}
