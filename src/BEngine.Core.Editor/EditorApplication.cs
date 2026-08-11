using System.Diagnostics;

namespace BEngine.Editor;

public enum PlayModeStateChange
{
    EnteredEditMode,
    ExitingEditMode,
    EnteredPlayMode,
    ExitingPlayMode
}

public enum PauseState
{
    Paused,
    Unpaused
}

public static class EditorApplication
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static Action? _delayCall;

    public static event Action? update;
    public static event Action? projectChanged;
    public static event Action? hierarchyChanged;
    public static event Action? quitting;
    public static event Action<PlayModeStateChange>? playModeStateChanged;
    public static event Action<PauseState>? pauseStateChanged;

    public static double timeSinceStartup => Clock.Elapsed.TotalSeconds;
    public static string applicationPath => Environment.ProcessPath ?? string.Empty;
    public static string applicationContentsPath => AppContext.BaseDirectory;
    public static bool isCompiling { get; internal set; }
    public static bool isUpdating { get; internal set; }
    public static bool isPlayingOrWillChangePlaymode => isPlaying;
    public static bool isPlaying
    {
        get => EditorBridge.Host?.IsPlaying ?? false;
        set
        {
            if (EditorBridge.Host is { } host) host.IsPlaying = value;
        }
    }

    public static bool isPaused
    {
        get => EditorBridge.Host?.IsPaused ?? false;
        set
        {
            if (EditorBridge.Host is { } host && host.IsPaused != value)
            {
                host.IsPaused = value;
                pauseStateChanged?.Invoke(value ? PauseState.Paused : PauseState.Unpaused);
            }
        }
    }

    public static Action? delayCall
    {
        get => _delayCall;
        set => _delayCall = value;
    }

    public static bool ExecuteMenuItem(string menuItemPath) =>
        EditorBridge.Host?.ExecuteMenuItem(menuItemPath) ?? false;

    public static void QueuePlayerLoopUpdate() => EditorBridge.Host?.RepaintAllWindows();
    public static void RepaintProjectWindow() => EditorBridge.Host?.RepaintAllWindows();
    public static void RepaintHierarchyWindow() => EditorBridge.Host?.RepaintAllWindows();
    public static void Beep() => Console.Write('\a');
    public static void Exit(int returnValue = 0) => EditorBridge.Host?.Exit(returnValue);
    public static void LockReloadAssemblies() { }
    public static void UnlockReloadAssemblies() { }

    internal static void RaiseUpdate()
    {
        update?.Invoke();
        var delayed = Interlocked.Exchange(ref _delayCall, null);
        delayed?.Invoke();
    }

    internal static void RaiseProjectChanged()
    {
        projectChanged?.Invoke();
        EditorWindow.NotifyProjectChanged();
    }

    internal static void RaiseHierarchyChanged()
    {
        hierarchyChanged?.Invoke();
        EditorWindow.NotifyHierarchyChanged();
    }
    internal static void RaiseQuitting() => quitting?.Invoke();
    internal static void RaisePlayModeStateChanged(PlayModeStateChange state) => playModeStateChanged?.Invoke(state);
}
