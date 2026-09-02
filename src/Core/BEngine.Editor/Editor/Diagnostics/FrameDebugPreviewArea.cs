using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public readonly record struct FrameDebugPreviewArea(
    GraphicsRect Region,
    int SurfaceWidth,
    int SurfaceHeight)
{
    public bool IsValid => Region.Width > 0 && Region.Height > 0 &&
                           SurfaceWidth > 0 && SurfaceHeight > 0;
}
