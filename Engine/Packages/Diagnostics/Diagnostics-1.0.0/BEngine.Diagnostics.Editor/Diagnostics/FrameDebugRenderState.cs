using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugRenderState(
    GraphicsRect Viewport,
    GraphicsRect? Scissor,
    GraphicsDepthState DepthState,
    GraphicsBlendMode BlendMode,
    GraphicsRasterizerState RasterizerState,
    string ProgramLabel,
    string RenderTargetLabel,
    IReadOnlyList<FrameDebugTextureBinding> Textures,
    IReadOnlyList<FrameDebugIntProperty>? Ints = null,
    IReadOnlyList<FrameDebugFloatProperty>? Floats = null,
    IReadOnlyList<FrameDebugVectorProperty>? Vectors = null,
    IReadOnlyList<FrameDebugMatrixProperty>? Matrices = null,
    FrameDebugProgramState? Program = null,
    FrameDebugRenderTargetState? RenderTarget = null);
