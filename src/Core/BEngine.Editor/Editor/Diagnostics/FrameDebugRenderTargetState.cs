using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugRenderTargetState(
    string Label,
    int Width,
    int Height,
    GraphicsTextureFormat? ColorFormat,
    GraphicsTextureFormat? DepthFormat,
    bool ColorSampled,
    bool DepthSampled);
