using BEngine.Platform;

namespace BEngine;

public static class Screen
{
    public static int width => currentResolution.width;
    public static int height => currentResolution.height;
    public static Resolution currentResolution => DisplayUtility.Current.currentResolution;
    public static Resolution[] resolutions { get => DisplayUtility.Current.resolutions; internal set => DisplayUtility.Current.resolutions = value; }
    public static Rect safeArea => DisplayUtility.Current.safeArea;
    public static event Action<Rect>? safeAreaChanged
    { add => DisplayUtility.Current.safeAreaChanged += value; remove => DisplayUtility.Current.safeAreaChanged -= value; }
    public static bool fullScreen
    {
        get => fullScreenMode != FullScreenMode.Windowed;
        set => fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }
    public static FullScreenMode fullScreenMode { get => DisplayUtility.Current.fullScreenMode; set => DisplayUtility.Current.fullScreenMode = value; }
    public static Fix64 dpi { get => DisplayUtility.Current.dpi; internal set => DisplayUtility.Current.dpi = value; }
    [Obsolete("Use Cursor.lockState instead.")]
    public static bool lockCursor
    {
        get => Cursor.lockState == CursorLockMode.Locked;
        set => Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
    }
    public static void SetResolution(int width, int height, bool fullscreen, int preferredRefreshRate = 60) =>
        SetResolution(width, height, fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed, preferredRefreshRate);
    public static void SetResolution(int width, int height, FullScreenMode fullscreenMode, int preferredRefreshRate = 60) =>
        DisplayUtility.Current.SetResolution(width, height, fullscreenMode, preferredRefreshRate);
}
