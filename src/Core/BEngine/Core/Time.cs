namespace BEngine;

public static class Time
{
    private static readonly System.Diagnostics.Stopwatch RealtimeClock =
        System.Diagnostics.Stopwatch.StartNew();
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

    public static Fix64 deltaTime
    {
        get { MainThreadGuard.Ensure(); return _deltaTime; }
        internal set => _deltaTime = value;
    }
    public static Fix64 unscaledDeltaTime
    {
        get { MainThreadGuard.Ensure(); return _unscaledDeltaTime; }
        internal set => _unscaledDeltaTime = value;
    }
    public static Fix64 fixedDeltaTime
    {
        get { MainThreadGuard.Ensure(); return _fixedDeltaTime; }
        set { MainThreadGuard.Ensure(); _fixedDeltaTime = value; }
    }
    public static Fix64 time
    {
        get { MainThreadGuard.Ensure(); return _time; }
        internal set => _time = value;
    }
    public static Fix64 unscaledTime
    {
        get { MainThreadGuard.Ensure(); return _unscaledTime; }
        internal set => _unscaledTime = value;
    }
    public static Fix64 fixedTime
    {
        get { MainThreadGuard.Ensure(); return _fixedTime; }
        internal set => _fixedTime = value;
    }
    public static Fix64 timeSinceLevelLoad
    {
        get { MainThreadGuard.Ensure(); return _time; }
    }
    public static Fix64 realtimeSinceStartup
    {
        get { MainThreadGuard.Ensure(); return (Fix64)RealtimeClock.Elapsed.TotalSeconds; }
    }
    public static Fix64 timeScale
    {
        get { MainThreadGuard.Ensure(); return _timeScale; }
        set { MainThreadGuard.Ensure(); _timeScale = value; }
    }
    public static Fix64 maximumDeltaTime
    {
        get { MainThreadGuard.Ensure(); return _maximumDeltaTime; }
        set { MainThreadGuard.Ensure(); _maximumDeltaTime = value; }
    }
    public static bool inFixedTimeStep
    {
        get { MainThreadGuard.Ensure(); return _inFixedTimeStep; }
        internal set => _inFixedTimeStep = value;
    }
    public static long frameCount
    {
        get { MainThreadGuard.Ensure(); return _frameCount; }
        internal set => _frameCount = value;
    }

    internal static void Reset()
    {
        _deltaTime = Fix64.Zero;
        _unscaledDeltaTime = Fix64.Zero;
        _time = Fix64.Zero;
        _unscaledTime = Fix64.Zero;
        _fixedTime = Fix64.Zero;
        _inFixedTimeStep = false;
        _frameCount = 0;
    }

    internal static Fix64 AdvanceFrameUnchecked(Fix64 sourceDeltaTime)
    {
        _unscaledDeltaTime = Fix64.Clamp(sourceDeltaTime, Fix64.Zero, _maximumDeltaTime);
        _deltaTime = _unscaledDeltaTime * Fix64.Max(Fix64.Zero, _timeScale);
        _time += _deltaTime;
        _unscaledTime += _unscaledDeltaTime;
        _frameCount++;
        return _deltaTime;
    }

    internal static Fix64 FixedDeltaTimeUnchecked => _fixedDeltaTime;

    internal static void BeginFixedStepUnchecked()
    {
        _inFixedTimeStep = true;
        _fixedTime += _fixedDeltaTime;
    }

    internal static void EndFixedStepUnchecked() => _inFixedTimeStep = false;
}
