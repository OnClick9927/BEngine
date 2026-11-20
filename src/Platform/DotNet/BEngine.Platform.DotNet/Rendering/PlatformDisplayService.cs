namespace BEngine.Platform;

/// <summary>State shared by platform hosts, including virtual editor viewports.</summary>
public sealed class PlatformDisplayService : IDisplayService
{
    private Resolution _resolution = new(1280, 720, 60);
    private Resolution[] _resolutions = [new(1280, 720, 60)];
    private FullScreenMode _mode = FullScreenMode.Windowed;
    public Resolution currentResolution => _resolution;
    public Resolution[] resolutions { get => [.. _resolutions]; set => _resolutions = value is null ? [] : [.. value]; }
    private Vector4 _insets;
    public event Action<Rect>? safeAreaChanged;
    public Rect safeArea => new(_insets.x, _insets.y,
        Fix64.Max(0, _resolution.width - _insets.x - _insets.z),
        Fix64.Max(0, _resolution.height - _insets.y - _insets.w));
    public void ReportSafeArea(Vector4 insets)
    {
        var normalized = new Vector4(Mathf.Clamp(insets.x, 0, _resolution.width), Mathf.Clamp(insets.y, 0, _resolution.height),
            Mathf.Clamp(insets.z, 0, _resolution.width), Mathf.Clamp(insets.w, 0, _resolution.height));
        if (_insets == normalized) return;
        _insets = normalized;
        safeAreaChanged?.Invoke(safeArea);
    }
    public Fix64 dpi { get; set; } = 96;
    public FullScreenMode fullScreenMode
    {
        get => _mode;
        set => SetResolution(_resolution.width, _resolution.height, value, _resolution.refreshRate);
    }
    public DisplayResolutionRequest? PendingResolution { get; private set; }
    public void SetResolution(int width, int height, FullScreenMode fullscreenMode, int preferredRefreshRate = 60)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var previous = safeArea;
        _resolution = new(width, height, Math.Max(1, preferredRefreshRate));
        _mode = fullscreenMode;
        PendingResolution = new(_resolution, fullscreenMode);
        if (!previous.Equals(safeArea)) safeAreaChanged?.Invoke(safeArea);
    }
    public void ReportDisplay(int width, int height, FullScreenMode fullscreenMode)
    {
        var previous = safeArea;
        _resolution = new(Math.Max(1, width), Math.Max(1, height), _resolution.refreshRate);
        _mode = fullscreenMode;
        if (!previous.Equals(safeArea)) safeAreaChanged?.Invoke(safeArea);
    }
}
