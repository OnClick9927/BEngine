using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugPreviewSnapshot(
    long CaptureId,
    int EventCount,
    GraphicsColorReadbackStatus Status,
    GraphicsColorReadbackImage? Image,
    string Error)
{
    public int Width => Image?.Width ?? 0;
    public int Height => Image?.Height ?? 0;
}
