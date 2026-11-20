namespace BEngine;

public static partial class Time
{
    /// <summary>An opaque simulation-clock snapshot. Realtime and the monotonically
    /// increasing frame sequence are intentionally not rewound.</summary>
    public readonly struct State
    {
        private readonly bool _initialized;
        private readonly Fix64 _delta, _unscaledDelta, _fixedDelta, _scaled, _unscaled, _fixed;
        private readonly Fix64 _scale, _maximum, _accumulator, _fixedStart, _frameDelta;
        private readonly long _frames;
        private readonly int _steps;

        private State(bool initialized)
        {
            _initialized = initialized;
            _delta = _deltaTime; _unscaledDelta = _unscaledDeltaTime; _fixedDelta = _fixedDeltaTime;
            _scaled = _time; _unscaled = _unscaledTime; _fixed = _fixedTime;
            _scale = _timeScale; _maximum = _maximumDeltaTime;
            _accumulator = _fixedAccumulator; _fixedStart = _frameFixedStart;
            _frameDelta = _frameFixedDelta; _frames = _frameCount; _steps = _frameFixedSteps;
        }

        internal static State Capture()
        {
            if (IsFrameActive || _inFixedTimeStep)
                throw new InvalidOperationException("Capture the simulation clock between runtime frames.");
            return new State(true);
        }

        internal void Restore()
        {
            if (!_initialized) throw new ArgumentException("An uninitialized clock state cannot be restored.");
            if (IsFrameActive || _inFixedTimeStep)
                throw new InvalidOperationException("Restore the simulation clock between runtime frames.");
            _deltaTime = _delta; _unscaledDeltaTime = _unscaledDelta; _fixedDeltaTime = _fixedDelta;
            _time = _scaled; _unscaledTime = _unscaled; _fixedTime = _fixed;
            _timeScale = _scale; _maximumDeltaTime = _maximum;
            _fixedAccumulator = _accumulator; _frameFixedStart = _fixedStart;
            _frameFixedDelta = _frameDelta; _frameCount = _frames; _frameFixedSteps = _steps;
        }
    }

    /// <summary>Captures simulation time, its settings and pending fixed-step remainder.
    /// Call only between frames; this operation does not allocate.</summary>
    public static State CaptureState() => State.Capture();

    /// <summary>Restores a captured clock between frames, including its fixed-step remainder.</summary>
    public static void RestoreState(State state) => state.Restore();
}
