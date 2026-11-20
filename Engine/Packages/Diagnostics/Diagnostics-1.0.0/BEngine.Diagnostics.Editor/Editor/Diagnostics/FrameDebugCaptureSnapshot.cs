using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugCaptureSnapshot(
    long CaptureId,
    string TargetName,
    GraphicsBackend Backend,
    DateTimeOffset CapturedAt,
    IReadOnlyList<FrameDebugEvent> Events);
