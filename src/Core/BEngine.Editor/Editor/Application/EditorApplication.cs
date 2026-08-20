using System.Diagnostics;

namespace BEngine.Editor;

public static class EditorApplication
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object DelayCallGate = new();
    private static Action? _delayCall;
    private static int _reloadLockCount;
    private static bool _reloadRequested;

    public static event Action? update;
    public static event Action? projectChanged;
    public static event Action? hierarchyChanged;
    public static event Action? quitting;
    public static event Action<PlayModeStateChange>? playModeStateChanged;
    public static event Action<PauseState>? pauseStateChanged;
    public static event Func<bool>? wantsToQuit;
    public static event Action<GenericMenu, SerializedProperty>? contextualPropertyMenu;

    public static double timeSinceStartup => Clock.Elapsed.TotalSeconds;
    public static string applicationPath => Environment.ProcessPath ?? string.Empty;
    public static string applicationContentsPath => AppContext.BaseDirectory;
    public static string projectPath => EditorBridge.Host?.ProjectRootPath ?? string.Empty;
    public static bool isCompiling { get; internal set; }
    public static bool isUpdating { get; internal set; }
    public static bool isAssemblyReloadLocked => Volatile.Read(ref _reloadLockCount) > 0;
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
                EditorCallbackDispatcher.Invoke(pauseStateChanged,
                    value ? PauseState.Paused : PauseState.Unpaused, nameof(pauseStateChanged));
            }
        }
    }

    public static event Action? delayCall
    {
        add
        {
            lock (DelayCallGate) _delayCall += value;
        }
        remove
        {
            lock (DelayCallGate) _delayCall -= value;
        }
    }

    public static bool ExecuteMenuItem(string menuItemPath) =>
        EditorBridge.Host?.ExecuteMenuItem(menuItemPath) ?? false;

    public static Task ScheduleBackgroundTask(
        string name,
        Action<CancellationToken> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default) =>
        RequireTaskScheduler().ScheduleAsync(name, operation, priority, cancellationToken);

    public static Task<T> ScheduleBackgroundTask<T>(
        string name,
        Func<CancellationToken, T> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default) =>
        RequireTaskScheduler().ScheduleAsync(name, operation, priority, cancellationToken);

    public static void QueueMainThread(Action callback, string? name = null) =>
        RequireTaskScheduler().Post(callback, name);

    public static void QueuePlayerLoopUpdate() => EditorBridge.Host?.RepaintAllWindows();
    public static void RepaintProjectWindow() => EditorBridge.Host?.RepaintAllWindows();
    public static void RepaintHierarchyWindow() => EditorBridge.Host?.RepaintAllWindows();
    public static void Beep() => Console.Write('\a');
    public static void Exit(int returnValue = 0) => EditorBridge.Host?.Exit(returnValue);
    public static void LockReloadAssemblies() => Interlocked.Increment(ref _reloadLockCount);
    public static void Step()
    {
        if (EditorBridge.Host is not { IsPlaying: true } host) return;
        var paused = host.IsPaused;
        host.IsPaused = false;
        host.RepaintAllWindows();
        host.IsPaused = paused;
    }

    public static void UnlockReloadAssemblies()
    {
        var count = Interlocked.Decrement(ref _reloadLockCount);
        if (count < 0)
        {
            Interlocked.Exchange(ref _reloadLockCount, 0);
            throw new InvalidOperationException("UnlockReloadAssemblies was called without a matching lock.");
        }
        if (count != 0 || !_reloadRequested) return;
        _reloadRequested = false;
        EditorBridge.Host?.RequestScriptCompilation();
    }

    internal static void RaiseUpdate()
    {
        EditorCallbackDispatcher.Invoke(update, nameof(update));
        Action? delayed;
        lock (DelayCallGate)
        {
            delayed = _delayCall;
            _delayCall = null;
        }
        EditorCallbackDispatcher.Invoke(delayed, nameof(delayCall));
    }

    internal static void RaiseProjectChanged()
    {
        EditorCallbackDispatcher.Invoke(projectChanged, nameof(projectChanged));
        EditorWindow.NotifyProjectChanged();
    }

    internal static void RaiseHierarchyChanged()
    {
        EditorCallbackDispatcher.Invoke(hierarchyChanged, nameof(hierarchyChanged));
        EditorWindow.NotifyHierarchyChanged();
    }
    internal static bool RaiseWantsToQuit()
    {
        if (wantsToQuit is null) return true;
        foreach (Func<bool> callback in wantsToQuit.GetInvocationList())
        {
            var method = callback.Method;
            var feature = $"Editor callback {nameof(wantsToQuit)} " +
                          $"[{method.Module.ModuleVersionId:N}:{method.MetadataToken}]";
            if (EditorFeatureGuard.TryInvoke(feature, callback, true, out var allowQuit) && !allowQuit)
                return false;
        }
        return true;
    }

    internal static void RaiseQuitting() =>
        EditorCallbackDispatcher.Invoke(quitting, nameof(quitting));

    internal static void RaisePlayModeStateChanged(PlayModeStateChange state) =>
        EditorCallbackDispatcher.Invoke(playModeStateChanged, state, nameof(playModeStateChanged));

    internal static void RequestReloadWhenUnlocked() => _reloadRequested = true;

    internal static IEditorTaskScheduler? TaskScheduler => EditorBridge.Host?.TaskScheduler;

    internal static void RaiseContextualPropertyMenu(GenericMenu menu, SerializedProperty property) =>
        EditorCallbackDispatcher.Invoke(contextualPropertyMenu, menu, property, nameof(contextualPropertyMenu));

    private static IEditorTaskScheduler RequireTaskScheduler() =>
        TaskScheduler ?? throw new InvalidOperationException(
            "The editor background scheduler is only available while an editor host is running.");
}
