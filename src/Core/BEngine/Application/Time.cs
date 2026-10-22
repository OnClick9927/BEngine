namespace BEngine;

public static class Time
{
    private static long _realtimeStartTimestamp =
        RuntimeServiceAccess.PlatformService.MonotonicTimestamp;
    private static Fix64 _deltaTime;
    private static Fix64 _unscaledDeltaTime;
    private static Fix64 _fixedDeltaTime = Fix64.Parse("0.02");
    private static Fix64 _time;
    private static Fix64 _unscaledTime;
    private static Fix64 _fixedTime;
    private static Fix64 _timeScale = Fix64.One;
    private static Fix64 _maximumDeltaTime = Fix64.Parse("0.333333333");
    private static bool _inFixedTimeStep;
    private static long _frameCount;
    private static Fix64 _fixedAccumulator;
    private static Fix64 _frameFixedStart;
    private static Fix64 _frameFixedDelta;
    private static int _frameFixedSteps;
    internal static bool IsFrameActive { get; private set; }
    internal static long FrameSequence { get; private set; }

    public static Fix64 deltaTime
    {
        get { return _inFixedTimeStep ? _frameFixedDelta : _deltaTime; }
        internal set => _deltaTime = value;
    }
    public static Fix64 unscaledDeltaTime
    {
        get { return _unscaledDeltaTime; }
        internal set => _unscaledDeltaTime = value;
    }
    public static Fix64 fixedDeltaTime
    {
        get { return _fixedDeltaTime; }
        set
        {
            if (value <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Fixed delta time must be positive.");
            _fixedDeltaTime = value;
        }
    }
    public static Fix64 time
    {
        get { return _inFixedTimeStep ? _fixedTime : _time; }
        internal set => _time = value;
    }
    public static Fix64 unscaledTime
    {
        get { return _unscaledTime; }
        internal set => _unscaledTime = value;
    }
    public static Fix64 fixedTime
    {
        get { return _fixedTime; }
        internal set => _fixedTime = value;
    }
    public static Fix64 timeSinceLevelLoad
    {
        get { return Fix64.Max(Fix64.Zero, time - (SceneRuntime.currentScene?.RuntimeLoadTime ?? Fix64.Zero)); }
    }
    public static Fix64 realtimeSinceStartup
    {
        get
        {
            return (Fix64)RuntimeServiceAccess.PlatformService
                .GetElapsedTime(_realtimeStartTimestamp).TotalSeconds;
        }
    }
    public static Fix64 timeScale
    {
        get { return _timeScale; }
        set
        {
            if (value < Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Time scale cannot be negative.");
            _timeScale = value;
        }
    }
    public static Fix64 maximumDeltaTime
    {
        get { return _maximumDeltaTime; }
        set
        {
            if (value <= Fix64.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum delta time must be positive.");
            _maximumDeltaTime = value;
        }
    }
    public static bool inFixedTimeStep
    {
        get { return _inFixedTimeStep; }
        internal set => _inFixedTimeStep = value;
    }
    public static long frameCount
    {
        get { return _frameCount; }
        internal set => _frameCount = value;
    }

    internal static void Reset()
    {
        _realtimeStartTimestamp = RuntimeServiceAccess.PlatformService.MonotonicTimestamp;
        _deltaTime = Fix64.Zero;
        _unscaledDeltaTime = Fix64.Zero;
        _time = Fix64.Zero;
        _unscaledTime = Fix64.Zero;
        _fixedTime = Fix64.Zero;
        _inFixedTimeStep = false;
        _frameCount = 0;
        _fixedAccumulator = Fix64.Zero;
        _frameFixedStart = Fix64.Zero;
        _frameFixedSteps = 0;
        IsFrameActive = false;
    }

    internal static Fix64 AdvanceFrameUnchecked(Fix64 sourceDeltaTime)
    {
        _unscaledDeltaTime = Fix64.Max(sourceDeltaTime, Fix64.Zero);
        var scaledRaw = ((Int128)_unscaledDeltaTime.RawValue * _timeScale.RawValue) >> Fix64.FractionalBits;
        _deltaTime = Fix64.FromRaw((long)Int128.Min(scaledRaw, _maximumDeltaTime.RawValue));
        _time += _deltaTime;
        _unscaledTime += _unscaledDeltaTime;
        _frameCount++;
        return _deltaTime;
    }

    internal static Fix64 FixedDeltaTimeUnchecked => _fixedDeltaTime;

    internal static int FrameFixedSteps => _frameFixedSteps;
    internal static Fix64 FrameFixedDelta => _frameFixedDelta;

    internal static void BeginFrame(Fix64 deltaTime)
    {
        if (IsFrameActive) throw new InvalidOperationException("A runtime frame cannot be nested.");
        FrameSequence++;
        AdvanceFrameUnchecked(deltaTime);
        _frameFixedStart = _fixedTime;
        _frameFixedDelta = _fixedDeltaTime;
        _fixedAccumulator += _deltaTime;
        _frameFixedSteps = (int)(_fixedAccumulator / _frameFixedDelta);
        _fixedAccumulator -= _frameFixedSteps * _frameFixedDelta;
        IsFrameActive = true;
    }

    internal static void EndFrame()
    {
        if (!IsFrameActive) return;
        _fixedTime = _frameFixedStart + _frameFixedSteps * _frameFixedDelta;
        _inFixedTimeStep = false;
        IsFrameActive = false;
    }

    internal static void BeginFixedStepUnchecked(int index)
    {
        _inFixedTimeStep = true;
        _fixedTime = _frameFixedStart + (index + 1) * _frameFixedDelta;
    }

    internal static void BeginFixedStepUnchecked()
    {
        _inFixedTimeStep = true;
        _fixedTime += _fixedDeltaTime;
    }

    internal static void EndFixedStepUnchecked() => _inFixedTimeStep = false;
}
