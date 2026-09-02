namespace BEngine.Editor;

internal readonly record struct GameViewFrameTimingSnapshot(
    double FramesPerSecond,
    double FrameTimeMilliseconds,
    long SampleCount)
{
    internal bool HasValue => SampleCount > 0;
}
