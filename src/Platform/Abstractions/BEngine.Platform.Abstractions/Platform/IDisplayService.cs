namespace BEngine.Platform;

/// <summary>Platform display SPI. Consumers use Screen or DisplayUtility.</summary>
public interface IDisplayService
{
    Resolution currentResolution { get; }
    Resolution[] resolutions { get; set; }
    Rect safeArea { get; }
    event Action<Rect>? safeAreaChanged { add { } remove { } }
    FullScreenMode fullScreenMode { get; set; }
    Fix64 dpi { get; set; }
    DisplayResolutionRequest? PendingResolution { get; }
    void SetResolution(int width, int height, FullScreenMode fullscreenMode, int preferredRefreshRate = 60);
    void ReportDisplay(int width, int height, FullScreenMode fullscreenMode);
    /// <summary>Insets in framebuffer pixels: left, bottom, right, top.</summary>
    void ReportSafeArea(Vector4 insets) { }
}
