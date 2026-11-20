using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugShaderStage(
    GraphicsShaderStage Stage,
    GraphicsShaderLanguage Language,
    string EntryPoint);
