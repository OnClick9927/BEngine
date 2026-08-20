namespace BEngine;

public static class Cursor
{
    private static bool _visible = true;
    private static CursorLockMode _lockState;
    private static BAsset? _texture;
    private static Vector2 _hotspot;
    private static CursorMode _mode;

    public static bool visible
    {
        get { MainThreadGuard.Ensure(); return _visible; }
        set { MainThreadGuard.Ensure(); _visible = value; }
    }
    public static CursorLockMode lockState
    {
        get { MainThreadGuard.Ensure(); return _lockState; }
        set { MainThreadGuard.Ensure(); _lockState = value; }
    }
    public static BAsset? texture
    {
        get { MainThreadGuard.Ensure(); return _texture; }
    }
    public static Vector2 hotspot
    {
        get { MainThreadGuard.Ensure(); return _hotspot; }
    }
    public static CursorMode mode
    {
        get { MainThreadGuard.Ensure(); return _mode; }
    }

    public static void SetCursor(BAsset? cursorTexture, Vector2 cursorHotspot, CursorMode cursorMode)
    {
        MainThreadGuard.Ensure();
        _texture = cursorTexture;
        _hotspot = cursorHotspot;
        _mode = cursorMode;
    }

    internal static CursorLockMode GetLockStateUnchecked() => _lockState;
    internal static void SetLockStateUnchecked(CursorLockMode value) => _lockState = value;
}
