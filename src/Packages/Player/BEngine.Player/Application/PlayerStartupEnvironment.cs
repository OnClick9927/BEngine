using System.Diagnostics;
using System.Threading;

namespace BEngine.Player;

/// <summary>Pollable startup state shared by native hosts. Preparing the environment never waits for the logo.</summary>
public sealed class PlayerStartupProgress
{
    public const double LogoFadeSeconds = 0.5;
    public const double CompletedProgressDisplaySeconds = 0.1;
    private long _started = Stopwatch.GetTimestamp();
    private readonly double _elapsedBeforeStart;
    private float _progress;
    private int _prepared;
    private int _presentationHeld;
    private int _progressVisible;
    private long _completedFrameTimestamp;
    private string _stage = "environment";

    public PlayerStartupProgress(TimeSpan elapsedBeforeStart = default) =>
        _elapsedBeforeStart = Math.Max(0, elapsedBeforeStart.TotalSeconds);

    public double ElapsedSeconds => _elapsedBeforeStart + Stopwatch.GetElapsedTime(_started).TotalSeconds;
    public float LogoOpacity => (float)Math.Clamp(ElapsedSeconds / LogoFadeSeconds, 0, 1);
    public float Progress => Volatile.Read(ref _progress);
    public string Stage => Volatile.Read(ref _stage);
    public string StageDescription => Stage switch
    {
        "project" => "正在读取工程信息", "settings" => "正在应用设置", "resources" => "正在读取资源索引",
        "managedCode" => "正在加载程序", "services" => "正在初始化服务", "activation" => "正在激活模块",
        "scene" => "正在准备启动场景", "ready" => "准备完成", _ => "正在准备环境"
    };
    public bool IsPrepared => Volatile.Read(ref _prepared) != 0;
    public bool ShowProgressBar
    {
        get
        {
            if (!IsPrepared && ElapsedSeconds >= LogoFadeSeconds) Volatile.Write(ref _progressVisible, 1);
            return Volatile.Read(ref _progressVisible) != 0;
        }
    }
    public bool CanContinue => IsPrepared && ElapsedSeconds >= LogoFadeSeconds &&
        Volatile.Read(ref _presentationHeld) == 0 && (!ShowProgressBar ||
            Volatile.Read(ref _completedFrameTimestamp) is var timestamp && timestamp != 0 &&
            Stopwatch.GetElapsedTime(timestamp).TotalSeconds >= CompletedProgressDisplaySeconds);
    public void HoldPresentation() => Volatile.Write(ref _presentationHeld, 1);
    public void ReleasePresentation() => Volatile.Write(ref _presentationHeld, 0);
    public void RestartPresentation() => Volatile.Write(ref _started, Stopwatch.GetTimestamp());

    /// <summary>Called after presenting the sampled progress, never merely after assigning 100%.</summary>
    public void ConfirmProgressPresented(float displayedProgress)
    {
        if (IsPrepared && ShowProgressBar && displayedProgress >= 1)
            Interlocked.CompareExchange(ref _completedFrameTimestamp, Stopwatch.GetTimestamp(), 0);
    }

    public void Report(float value, string stage)
    {
        if (!float.IsFinite(value) || value < Progress) return;
        Volatile.Write(ref _stage, stage);
        Report(value);
    }

    public void Report(float value)
    {
        if (!float.IsFinite(value)) return;
        value = Math.Clamp(value, 0, 1);
        float previous;
        do { previous = Volatile.Read(ref _progress); if (value <= previous) return; }
        while (Interlocked.CompareExchange(ref _progress, value, previous) != previous);
    }

    public void Complete()
    {
        if (!IsPrepared && ElapsedSeconds >= LogoFadeSeconds) Volatile.Write(ref _progressVisible, 1);
        Report(1);
        Volatile.Write(ref _stage, "ready");
        Volatile.Write(ref _prepared, 1);
    }
}

/// <summary>Build-generated presentation data; only Player hosts start this flow, never the Editor.</summary>
public static class PlayerStartupEnvironment
{
    public static byte[] LogoPng { get; private set; } = [];
    public static string BackgroundColor { get; private set; } = "#10161A";
    public static PlayerStartupProgress? Current { get; private set; }

    public static void Configure(string logoBase64, string backgroundColor)
    {
        LogoPng = Convert.FromBase64String(logoBase64);
        BackgroundColor = backgroundColor;
    }

    public static PlayerStartupProgress Begin(TimeSpan elapsedBeforeStart = default) =>
        Current = new PlayerStartupProgress(elapsedBeforeStart);
}
