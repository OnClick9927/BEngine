namespace BEngine.Editor;

public readonly record struct ImGuiNativeFrameProfile(
    double DeltaSeconds,
    double BackgroundMilliseconds,
    double LayoutMilliseconds,
    double InputMilliseconds,
    double RepaintMilliseconds,
    double CanvasMilliseconds,
    double PresentMilliseconds,
    double TotalMilliseconds);
