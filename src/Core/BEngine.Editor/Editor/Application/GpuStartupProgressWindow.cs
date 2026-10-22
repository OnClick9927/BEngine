using System.Collections.Concurrent;
using System.Diagnostics;

namespace BEngine.Editor;

/// <summary>Keeps project-opening progress visible before the main editor window exists.</summary>
internal sealed class GpuStartupProgressWindow : IDisposable
{
    private static readonly long MinimumPumpIntervalTicks = Math.Max(1, Stopwatch.Frequency / 20);
    private readonly ConcurrentQueue<EditorProgressInfo> _pending = new();
    private readonly ExecutionUtility _executionEnvironment;
    private readonly int _ownerThreadId;
    private readonly NativeWindow _window;
    private readonly IDisposable _platformProgressSuppression;
    private EditorProgressInfo _state = new("打开项目", "正在准备编辑器...", 0, true, false, false);
    private long _lastPumpTimestamp;
    private bool _hasPumped;
    private bool _disposed;

    private GpuStartupProgressWindow(
        NativeWindowUtility nativeWindowFactory,
        ExecutionUtility executionEnvironment)
    {
        ArgumentNullException.ThrowIfNull(nativeWindowFactory);
        _executionEnvironment = executionEnvironment ??
                                throw new ArgumentNullException(nameof(executionEnvironment));
        _ownerThreadId = _executionEnvironment.CurrentManagedThreadId;
        _window = nativeWindowFactory.Create("BEngine - 打开项目", 560, 200, vsync: false);
        _platformProgressSuppression = EditorUtility.SuppressPlatformProgress();
        _window.gui += OnGUI;
        EditorUtility.progressChanged += OnProgressChanged;
        try
        {
            _window.Initialize();
            Pump(force: true);
            _window.Focus();
        }
        catch
        {
            EditorUtility.progressChanged -= OnProgressChanged;
            _window.Dispose();
            _platformProgressSuppression.Dispose();
            throw;
        }
    }

    public static GpuStartupProgressWindow Show(NativeWindowUtility nativeWindowFactory) =>
        new(nativeWindowFactory, EditorHostServices.Execution);

    internal static GpuStartupProgressWindow Show(
        NativeWindowUtility nativeWindowFactory,
        ExecutionUtility executionEnvironment) =>
        new(nativeWindowFactory, executionEnvironment);

    public void Complete()
    {
        if (_disposed) return;
        _state = new EditorProgressInfo("打开项目", "编辑器已就绪", 1, true, false, false);
        _window.Repaint();
        _window.Pump();
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EditorUtility.progressChanged -= OnProgressChanged;
        if (!_window.isClosing) _window.Close();
        _window.Dispose();
        _platformProgressSuppression.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnProgressChanged(EditorProgressInfo progress)
    {
        if (_disposed || !progress.IsVisible) return;
        _pending.Enqueue(progress);
        if (_executionEnvironment.CurrentManagedThreadId == _ownerThreadId) Pump();
    }

    private void Pump(bool force = false)
    {
        var previousState = _state;
        while (_pending.TryDequeue(out var progress)) _state = progress;
        var criticalChange = !string.Equals(previousState.Title, _state.Title, StringComparison.Ordinal) ||
                             previousState.IsCancelable != _state.IsCancelable ||
                             previousState.IsCancellationRequested != _state.IsCancellationRequested;
        var timestamp = Stopwatch.GetTimestamp();
        if (!force && !criticalChange && _hasPumped &&
            timestamp - _lastPumpTimestamp < MinimumPumpIntervalTicks) return;
        _lastPumpTimestamp = timestamp;
        _hasPumped = true;
        if (force || criticalChange) _window.Repaint();
        _window.Pump();
    }

    private void OnGUI()
    {
        DrawProgress(new Rect(0, 0, _window.width, _window.height), _state);
    }

    internal static void DrawProgress(Rect bounds, EditorProgressInfo state)
    {
        GUI.Box(bounds, GUIContent.none, GUI.skin.window);
        GUI.Label(new Rect(bounds.x + 24, bounds.y + 22, Fix64.Max(0, bounds.width - 48), 32),
            new GUIContent(state.Title, "Icons/BEngine.png"), EditorStyles.largeLabel);
        GUI.Label(new Rect(bounds.x + 24, bounds.y + 68, Fix64.Max(0, bounds.width - 48), 24),
            state.Info);
        var bar = new Rect(bounds.x + 24, bounds.y + 108, Fix64.Max(0, bounds.width - 48), 24);
        GUI.Box(bar, GUIContent.none, EditorStyles.progressBarBack);
        var normalizedProgress = Math.Clamp(state.Progress, 0, 1);
        var progress = (Fix64)normalizedProgress;
        GUI.Box(new Rect(bar.x, bar.y, bar.width * progress, bar.height), GUIContent.none,
            EditorStyles.progressBarBar);
        GUI.Label(bar, $"{normalizedProgress:P0}", EditorStyles.progressBarText);
    }
}
