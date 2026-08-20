namespace BEngine;

public static class Screen
{
    private static Resolution _currentResolution = new(1280, 720, 60);
    private static FullScreenMode _fullScreenMode = FullScreenMode.Windowed;
    private static Resolution[] _resolutions = [_currentResolution];
    private static Fix64 _dpi = 96;

    public static int width
    {
        get { MainThreadGuard.Ensure(); return _currentResolution.width; }
    }
    public static int height
    {
        get { MainThreadGuard.Ensure(); return _currentResolution.height; }
    }
    public static Resolution currentResolution
    {
        get { MainThreadGuard.Ensure(); return _currentResolution; }
    }
    public static Resolution[] resolutions
    {
        get { MainThreadGuard.Ensure(); return (Resolution[])_resolutions.Clone(); }
        internal set => _resolutions = value is null ? [] : (Resolution[])value.Clone();
    }
    public static Rect safeArea
    {
        get
        {
            MainThreadGuard.Ensure();
            return new Rect(0, 0, _currentResolution.width, _currentResolution.height);
        }
    }
    public static bool fullScreen
    {
        get { MainThreadGuard.Ensure(); return _fullScreenMode != FullScreenMode.Windowed; }
        set
        {
            MainThreadGuard.Ensure();
            _fullScreenMode = value ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        }
    }
    public static FullScreenMode fullScreenMode
    {
        get { MainThreadGuard.Ensure(); return _fullScreenMode; }
        set { MainThreadGuard.Ensure(); _fullScreenMode = value; }
    }
    [Obsolete("Use Cursor.lockState instead.")]
    public static bool lockCursor
    {
        get
        {
            MainThreadGuard.Ensure();
            return Cursor.GetLockStateUnchecked() == CursorLockMode.Locked;
        }
        set
        {
            MainThreadGuard.Ensure();
            Cursor.SetLockStateUnchecked(value ? CursorLockMode.Locked : CursorLockMode.None);
        }
    }
    public static Fix64 dpi
    {
        get { MainThreadGuard.Ensure(); return _dpi; }
        internal set => _dpi = value;
    }

    public static void SetResolution(int width, int height, bool fullscreen, int preferredRefreshRate = 60)
    {
        MainThreadGuard.Ensure();
        SetResolutionCore(width, height,
            fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed, preferredRefreshRate);
    }

    public static void SetResolution(int width, int height, FullScreenMode fullscreenMode,
        int preferredRefreshRate = 60)
    {
        MainThreadGuard.Ensure();
        SetResolutionCore(width, height, fullscreenMode, preferredRefreshRate);
    }

    private static void SetResolutionCore(int width, int height, FullScreenMode fullscreenMode,
        int preferredRefreshRate)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _currentResolution = new Resolution(width, height, Math.Max(1, preferredRefreshRate));
        _fullScreenMode = fullscreenMode;
    }
}
