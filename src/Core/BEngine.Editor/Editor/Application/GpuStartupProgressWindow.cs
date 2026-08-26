using System.Collections.Concurrent;

namespace BEngine.Editor;

/// <summary>Keeps project-opening progress visible before the main editor window exists.</summary>
internal sealed class GpuStartupProgressWindow : IDisposable
{
    private readonly ConcurrentQueue<EditorProgressInfo> _pending = new();
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly ImGuiNativeWindow _window;
    private readonly IDisposable _platformProgressSuppression;
    private EditorProgressInfo _state = new("打开项目", "正在准备编辑器...", 0, true, false, false);
    private bool _disposed;

    private GpuStartupProgressWindow()
    {
        _window = new ImGuiNativeWindow("BEngine - 打开项目", 560, 200);
        _platformProgressSuppression = EditorUtility.SuppressPlatformProgress();
        _window.gui += OnGUI;
        EditorUtility.progressChanged += OnProgressChanged;
        try
        {
            _window.Initialize();
            _window.Pump();
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

    public static GpuStartupProgressWindow Show() => new();

    public void Complete()
    {
        if (_disposed) return;
        _state = new EditorProgressInfo("打开项目", "编辑器已就绪", 1, true, false, false);
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
        if (Environment.CurrentManagedThreadId == _ownerThreadId) Pump();
    }

    private void Pump()
    {
        while (_pending.TryDequeue(out var progress)) _state = progress;
        _window.Pump();
    }

    private void OnGUI()
    {
        GUI.DrawRect(new Rect(0, 0, _window.width, _window.height),
            new Color(Fix64.FromDecimal(0.18m), Fix64.FromDecimal(0.18m), Fix64.FromDecimal(0.18m), 1));
        GUI.Label(new Rect(24, 22, _window.width - 48, 32),
            new GUIContent(_state.Title, "Icons/BEngine.png"), EditorStyles.largeLabel);
        GUI.Label(new Rect(24, 68, _window.width - 48, 24), _state.Info);
        var bar = new Rect(24, 108, _window.width - 48, 24);
        GUI.DrawRect(bar, new Color(Fix64.FromDecimal(0.10m), Fix64.FromDecimal(0.10m),
            Fix64.FromDecimal(0.10m), 1));
        GUI.DrawRect(new Rect(bar.x, bar.y, bar.width * (Fix64)Math.Clamp(_state.Progress, 0, 1), bar.height),
            new Color(Fix64.FromDecimal(0.18m), Fix64.FromDecimal(0.48m), Fix64.FromDecimal(0.72m), 1));
        GUI.Label(bar, $"{_state.Progress:P0}");
    }
}
