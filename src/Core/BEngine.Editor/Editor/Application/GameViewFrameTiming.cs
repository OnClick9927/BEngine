namespace BEngine.Editor;

internal sealed class GameViewFrameTiming
{
    private const double NewSampleWeight = 0.1;
    private double _smoothedSeconds;
    private long _sampleCount;

    internal GameViewFrameTimingSnapshot snapshot => _sampleCount == 0
        ? default
        : new GameViewFrameTimingSnapshot(
            1d / _smoothedSeconds,
            _smoothedSeconds * 1000d,
            _sampleCount);

    internal void RecordSample(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds <= 0) return;
        _smoothedSeconds = _sampleCount == 0
            ? seconds
            : _smoothedSeconds + (seconds - _smoothedSeconds) * NewSampleWeight;
        _sampleCount++;
    }
}
