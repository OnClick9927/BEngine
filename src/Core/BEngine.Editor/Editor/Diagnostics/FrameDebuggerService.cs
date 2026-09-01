using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Diagnostics;

public sealed record FrameDebugPreviewSnapshot(
    long CaptureId,
    int EventCount,
    GraphicsColorReadbackStatus Status,
    GraphicsColorReadbackImage? Image,
    string Error)
{
    public int Width => Image?.Width ?? 0;
    public int Height => Image?.Height ?? 0;
}

public interface IFrameDebuggerPlayMode
{
    bool IsPlaying { get; }
    bool IsPaused { get; set; }
}

/// <summary>
/// Coordinates the Frame Debugger window and render loop without exposing mutable capture buffers.
/// All state access is synchronized; the published snapshot is immutable after publication.
/// </summary>
public sealed class FrameDebuggerService
{
    private readonly object _gate = new();
    private readonly IFrameDebuggerPlayMode _playMode;
    private object? _target;
    private string _targetName = string.Empty;
    private FrameDebugCaptureSnapshot? _snapshot;
    private FrameDebugPreviewSnapshot? _preview;
    private GraphicsColorReadbackRequest? _previewRequest;
    private long _captureId;
    private long _captureGeneration;
    private long _version;
    private long _previewGeneration;
    private int _stepLimit = -1;
    private bool _enabled;
    private bool _captureRequested;
    private bool _captureInFlight;
    private bool _previewRequested;
    private bool _previewInFlight;
    private bool _pausedByService;

    public static FrameDebuggerService Shared { get; } = CreateSharedService();

    public FrameDebuggerService(IFrameDebuggerPlayMode? playMode = null)
    {
        _playMode = playMode ?? EditorPlayMode.Instance;
    }

    private static FrameDebuggerService CreateSharedService()
    {
        var service = new FrameDebuggerService();
        global::BEngine.Editor.EditorApplication.pauseStateChanged += state =>
        {
            if (state == global::BEngine.Editor.PauseState.Unpaused)
                service.SynchronizePlayModeState();
        };
        global::BEngine.Editor.EditorApplication.playModeStateChanged += state =>
        {
            if (state == global::BEngine.Editor.PlayModeStateChange.EnteredPlayMode)
                service.SynchronizePlayModeState();
        };
        return service;
    }

    public event Action? Changed;

    public bool Enabled
    {
        get { lock (_gate) return _enabled; }
    }

    public bool CapturePending
    {
        get { lock (_gate) return _captureRequested || _captureInFlight; }
    }

    /// <summary>
    /// Returns whether the render host must draw this target to satisfy a queued capture or
    /// selected-step preview. A null selected target means the first rendered Game view may claim it.
    /// </summary>
    internal bool RequiresRender(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            if (!_enabled || (_target is not null && !ReferenceEquals(_target, target))) return false;
            return (_captureRequested && !_captureInFlight) ||
                   (_previewRequested && !_previewInFlight && _snapshot is not null);
        }
    }

    public long Version
    {
        get { lock (_gate) return _version; }
    }

    /// <summary>
    /// -1 previews the completed frame; otherwise requests one debugger-only color readback after
    /// this many events. Rendering always continues to the completed frame.
    /// </summary>
    public int StepLimit
    {
        get { lock (_gate) return _stepLimit; }
    }

    public string TargetName
    {
        get { lock (_gate) return _targetName; }
    }

    public FrameDebugCaptureSnapshot? Snapshot
    {
        get { lock (_gate) return _snapshot; }
    }

    public FrameDebugPreviewSnapshot? Preview
    {
        get
        {
            ResolvePreview();
            lock (_gate) return _preview;
        }
    }

    public bool PausedPlayMode
    {
        get { lock (_gate) return _pausedByService; }
    }

    public bool IsTarget(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate) return ReferenceEquals(_target, target);
    }

    public void Enable()
    {
        var changed = false;
        lock (_gate)
        {
            if (!_enabled)
            {
                _enabled = true;
                _version++;
                changed = true;
            }
            PausePlayModeCore();
        }
        if (changed) RaiseChanged();
    }

    public void Enable(object target, string targetName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        lock (_gate)
        {
            InvalidateCaptureCore();
            _enabled = true;
            SelectTargetCore(target, targetName);
            PausePlayModeCore();
            _version++;
        }
        RaiseChanged();
    }

    public void SelectTarget(object target, string targetName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        var changed = false;
        lock (_gate)
        {
            if (ReferenceEquals(_target, target) &&
                string.Equals(_targetName, targetName, StringComparison.Ordinal)) return;
            InvalidateCaptureCore();
            SelectTargetCore(target, targetName);
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    /// <summary>Releases a closed render target and captures the next available target.</summary>
    public void ReleaseTarget(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var changed = false;
        lock (_gate)
        {
            if (!ReferenceEquals(_target, target)) return;
            _target = null;
            _targetName = string.Empty;
            _snapshot = null;
            InvalidateCaptureCore();
            _stepLimit = -1;
            if (_enabled) _captureRequested = true;
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public void Disable()
    {
        var changed = false;
        var resumePlayMode = false;
        lock (_gate)
        {
            if (!_enabled && !_captureRequested && _stepLimit < 0 && _target is null) return;
            _enabled = false;
            _captureRequested = false;
            InvalidateCaptureCore();
            _stepLimit = -1;
            _target = null;
            _targetName = string.Empty;
            resumePlayMode = _pausedByService;
            _pausedByService = false;
            _version++;
            changed = true;
        }
        if (resumePlayMode && _playMode.IsPlaying && _playMode.IsPaused)
            _playMode.IsPaused = false;
        if (changed) RaiseChanged();
    }

    public void RequestCapture()
    {
        lock (_gate)
        {
            InvalidateCaptureCore();
            _enabled = true;
            _captureRequested = true;
            _stepLimit = -1;
            PausePlayModeCore();
            _version++;
        }
        RaiseChanged();
    }

    /// <summary>
    /// Clears a live debugging session when Play Mode is resumed by the user. A frame capture
    /// intentionally owns the paused render state and must never remain active during playback.
    /// </summary>
    public void SynchronizePlayModeState()
    {
        if (!_playMode.IsPlaying || _playMode.IsPaused) return;
        var changed = false;
        lock (_gate)
        {
            if (!_enabled && !_captureRequested && !_captureInFlight && _snapshot is null &&
                _preview is null) return;
            _enabled = false;
            _captureRequested = false;
            InvalidateCaptureCore();
            _snapshot = null;
            _stepLimit = -1;
            _target = null;
            _targetName = string.Empty;
            _pausedByService = false;
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public void SetStepLimit(int eventCount)
    {
        if (eventCount < -1) throw new ArgumentOutOfRangeException(nameof(eventCount));
        var changed = false;
        lock (_gate)
        {
            if (eventCount >= 0 && _snapshot is { } snapshot)
                eventCount = Math.Min(eventCount, snapshot.Events.Count);
            if (_stepLimit == eventCount) return;
            InvalidatePreviewCore(clearPreview: true);
            _stepLimit = eventCount;
            _previewRequested = _snapshot is not null;
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    public void Clear()
    {
        var changed = false;
        lock (_gate)
        {
            if (_snapshot is null && !_captureRequested && !_captureInFlight && _stepLimit < 0) return;
            _snapshot = null;
            _captureRequested = false;
            InvalidateCaptureCore();
            _stepLimit = -1;
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    /// <summary>
    /// Opens a capture or debugger-preview render scope for the selected target. The first target rendered
    /// after parameterless <see cref="Enable()"/> is selected automatically.
    /// </summary>
    public RenderLease BeginRender(
        object target,
        FrameDebugGraphicsDevice device,
        string targetName) =>
        BeginRender(target, device, targetName, default);

    public RenderLease BeginRender(
        object target,
        FrameDebugGraphicsDevice device,
        string targetName,
        FrameDebugPreviewArea previewArea)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        if (!Enabled) return default;

        bool capture;
        bool preview;
        long captureGeneration;
        long previewGeneration;
        int stepLimit;
        lock (_gate)
        {
            if (!_enabled) return default;
            if (_target is null) SelectTargetCore(target, targetName, resetCaptureRequest: false);
            if (!ReferenceEquals(_target, target)) return default;
            capture = _captureRequested && !_captureInFlight;
            captureGeneration = _captureGeneration;
            if (capture)
            {
                _captureRequested = false;
                _captureInFlight = true;
            }
            preview = !capture && _previewRequested && !_previewInFlight && _snapshot is not null;
            if (preview)
            {
                _previewRequested = false;
                _previewInFlight = true;
            }
            previewGeneration = _previewGeneration;
            stepLimit = capture ? -1 : _stepLimit;
            if (!capture && !preview) return default;
        }

        try
        {
            var deviceScope = device.BeginDebugFrame(capture, stepLimit, previewArea);
            return new RenderLease(
                this, device, deviceScope, capture, preview, target, targetName,
                captureGeneration, previewGeneration, stepLimit);
        }
        catch
        {
            if (capture) RestoreCaptureRequest(target, captureGeneration);
            if (preview) RestorePreviewRequest(target, previewGeneration);
            throw;
        }
    }

    private void CompleteCapture(
        FrameDebugGraphicsDevice device,
        FrameDebugGraphicsDevice.DeviceFrameScope deviceScope,
        bool capture,
        bool preview,
        object target,
        long captureGeneration,
        long previewGeneration,
        int previewEventCount,
        string targetName)
    {
        var result = deviceScope.Complete();
        var publish = false;
        GraphicsColorReadbackRequest? discardedRequest = null;
        lock (_gate)
        {
            if (capture)
            {
                if (!_captureInFlight || captureGeneration != _captureGeneration ||
                    !ReferenceEquals(_target, target) || !_enabled)
                {
                    discardedRequest = result.PreviewRequest;
                }
                else
                {
                    _captureInFlight = false;
                    _snapshot = new FrameDebugCaptureSnapshot(
                        ++_captureId,
                        targetName,
                        device.Backend,
                        DateTimeOffset.Now,
                        Array.AsReadOnly(result.Events));
                    _stepLimit = -1;
                    InstallPreviewRequestCore(result.PreviewRequest, _captureId, result.Events.Length);
                    _version++;
                    publish = true;
                }
            }
            else if (preview)
            {
                if (!_previewInFlight || previewGeneration != _previewGeneration ||
                    !ReferenceEquals(_target, target) || !_enabled || _snapshot is null)
                {
                    discardedRequest = result.PreviewRequest;
                }
                else
                {
                    _previewInFlight = false;
                    InstallPreviewRequestCore(
                        result.PreviewRequest,
                        _snapshot.CaptureId,
                        previewEventCount < 0 ? _snapshot.Events.Count : previewEventCount);
                    _version++;
                    publish = true;
                }
            }
        }
        discardedRequest?.Dispose();
        if (publish) RaiseChanged();
    }

    private void InstallPreviewRequestCore(
        GraphicsColorReadbackRequest? request,
        long captureId,
        int eventCount)
    {
        _previewRequest?.Dispose();
        _previewRequest = request;
        if (request is null)
        {
            _preview = new FrameDebugPreviewSnapshot(
                captureId,
                eventCount,
                GraphicsColorReadbackStatus.Unavailable,
                null,
                "The graphics device did not create a preview request.");
            return;
        }
        _preview = new FrameDebugPreviewSnapshot(
            captureId,
            eventCount,
            request.Status,
            null,
            request.Error);
    }

    private void RestoreCaptureRequest(object target, long captureGeneration)
    {
        var changed = false;
        lock (_gate)
        {
            if (!_captureInFlight || captureGeneration != _captureGeneration ||
                !ReferenceEquals(_target, target)) return;
            _captureInFlight = false;
            if (_enabled) _captureRequested = true;
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    private void RestorePreviewRequest(object target, long previewGeneration)
    {
        var changed = false;
        lock (_gate)
        {
            if (!_previewInFlight || previewGeneration != _previewGeneration ||
                !ReferenceEquals(_target, target)) return;
            _previewInFlight = false;
            if (_enabled && _snapshot is not null) _previewRequested = true;
            _version++;
            changed = true;
        }
        if (changed) RaiseChanged();
    }

    private void ResolvePreview()
    {
        GraphicsColorReadbackRequest? request;
        FrameDebugPreviewSnapshot? current;
        long generation;
        lock (_gate)
        {
            request = _previewRequest;
            current = _preview;
            generation = _previewGeneration;
        }
        if (request is null || current is null) return;

        GraphicsColorReadbackImage? image = null;
        var ready = request.TryGetResult(out image);
        var status = request.Status;
        var error = request.Error;
        if (!ready && status == GraphicsColorReadbackStatus.Pending) return;

        var changed = false;
        lock (_gate)
        {
            if (!ReferenceEquals(_previewRequest, request) || generation != _previewGeneration) return;
            _previewRequest = null;
            _preview = current with
            {
                Status = ready ? GraphicsColorReadbackStatus.Ready : status,
                Image = ready ? image : null,
                Error = ready ? string.Empty : error
            };
            _version++;
            changed = true;
        }
        request.Dispose();
        if (changed) RaiseChanged();
    }

    private void InvalidateCaptureCore()
    {
        _captureGeneration = unchecked(_captureGeneration + 1);
        _captureInFlight = false;
        InvalidatePreviewCore(clearPreview: true);
    }

    private void InvalidatePreviewCore(bool clearPreview)
    {
        _previewGeneration = unchecked(_previewGeneration + 1);
        _previewRequested = false;
        _previewInFlight = false;
        _previewRequest?.Dispose();
        _previewRequest = null;
        if (clearPreview) _preview = null;
    }

    private void SelectTargetCore(
        object target,
        string targetName,
        bool resetCaptureRequest = true)
    {
        _target = target;
        _targetName = targetName;
        if (resetCaptureRequest) _captureRequested = false;
        _stepLimit = -1;
    }

    private void PausePlayModeCore()
    {
        if (!_playMode.IsPlaying || _playMode.IsPaused) return;
        _playMode.IsPaused = true;
        _pausedByService = true;
    }

    private void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null) return;
        foreach (Action callback in handler.GetInvocationList())
        {
            try { callback(); }
            catch { }
        }
    }

    public struct RenderLease : IDisposable
    {
        private FrameDebuggerService? _service;
        private readonly FrameDebugGraphicsDevice? _device;
        private FrameDebugGraphicsDevice.DeviceFrameScope _deviceScope;
        private readonly bool _capture;
        private readonly bool _preview;
        private readonly object _target;
        private readonly string _targetName;
        private readonly long _captureGeneration;
        private readonly long _previewGeneration;
        private readonly int _previewEventCount;

        internal RenderLease(
            FrameDebuggerService service,
            FrameDebugGraphicsDevice device,
            FrameDebugGraphicsDevice.DeviceFrameScope deviceScope,
            bool capture,
            bool preview,
            object target,
            string targetName,
            long captureGeneration,
            long previewGeneration,
            int previewEventCount)
        {
            _service = service;
            _device = device;
            _deviceScope = deviceScope;
            _capture = capture;
            _preview = preview;
            _target = target;
            _targetName = targetName;
            _captureGeneration = captureGeneration;
            _previewGeneration = previewGeneration;
            _previewEventCount = previewEventCount;
        }

        public void Dispose()
        {
            var service = _service;
            _service = null;
            if (service is null || _device is null)
            {
                _deviceScope.Dispose();
                return;
            }
            service.CompleteCapture(_device, _deviceScope, _capture, _preview,
                _target, _captureGeneration, _previewGeneration, _previewEventCount, _targetName);
            _deviceScope = default;
        }
    }

    private sealed class EditorPlayMode : IFrameDebuggerPlayMode
    {
        internal static readonly EditorPlayMode Instance = new();
        public bool IsPlaying => global::BEngine.Editor.EditorApplication.isPlaying;
        public bool IsPaused
        {
            get => global::BEngine.Editor.EditorApplication.isPaused;
            set => global::BEngine.Editor.EditorApplication.isPaused = value;
        }
    }
}
