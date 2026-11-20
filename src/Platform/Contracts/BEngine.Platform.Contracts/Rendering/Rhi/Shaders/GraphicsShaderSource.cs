namespace BEngine.Rendering.Rhi;

public readonly record struct GraphicsShaderSource(
    GraphicsShaderStage Stage,
    GraphicsShaderLanguage Language,
    string Code,
    string EntryPoint = "main");
