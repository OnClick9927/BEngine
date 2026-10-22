namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IDisplayService through dependency injection.</summary>
public sealed class DisplayUtility
{
    private readonly IDisplayService _implementation;

    public DisplayUtility(IDisplayService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static DisplayUtility Current => RuntimeServiceAccess.GetRequiredService<DisplayUtility>();

    public Resolution currentResolution => _implementation.currentResolution;

    public Resolution[] resolutions { get => _implementation.resolutions; set => _implementation.resolutions = value; }

    public Rect safeArea => _implementation.safeArea;
    public event Action<Rect>? safeAreaChanged
    { add => _implementation.safeAreaChanged += value; remove => _implementation.safeAreaChanged -= value; }
    public void ReportSafeArea(Vector4 insets) => _implementation.ReportSafeArea(insets);

    public FullScreenMode fullScreenMode { get => _implementation.fullScreenMode; set => _implementation.fullScreenMode = value; }

    public Fix64 dpi { get => _implementation.dpi; set => _implementation.dpi = value; }

    public DisplayResolutionRequest? PendingResolution => _implementation.PendingResolution;

    public void SetResolution(int width, int height, FullScreenMode fullscreenMode, int preferredRefreshRate = 60) =>
        _implementation.SetResolution(width, height, fullscreenMode, preferredRefreshRate);

    public void ReportDisplay(int width, int height, FullScreenMode fullscreenMode) =>
        _implementation.ReportDisplay(width, height, fullscreenMode);
}
