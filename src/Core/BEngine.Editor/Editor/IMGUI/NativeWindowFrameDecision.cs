namespace BEngine.Editor;

internal readonly record struct NativeWindowFrameDecision(
    bool ShouldRender,
    long Timestamp,
    long RequestVersion);
