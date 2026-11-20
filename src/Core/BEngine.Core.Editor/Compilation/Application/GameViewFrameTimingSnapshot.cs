namespace BEngine.Editor;

public readonly record struct GameViewFrameTimingSnapshot(
    double FramesPerSecond,
    double FrameTimeMilliseconds,
    long SampleCount)
{
    public bool HasValue => SampleCount > 0;
}
