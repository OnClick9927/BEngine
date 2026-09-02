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
        get { return _visible; }
        set { _visible = value; }
    }
    public static CursorLockMode lockState
    {
        get { return _lockState; }
        set { _lockState = value; }
    }
    public static BAsset? texture
    {
        get { return _texture; }
    }
    public static Vector2 hotspot
    {
        get { return _hotspot; }
    }
    public static CursorMode mode
    {
        get { return _mode; }
    }

    public static void SetCursor(BAsset? cursorTexture, Vector2 cursorHotspot, CursorMode cursorMode)
    {
        _texture = cursorTexture;
        _hotspot = cursorHotspot;
        _mode = cursorMode;
    }

    internal static CursorLockMode GetLockStateUnchecked() => _lockState;
    internal static void SetLockStateUnchecked(CursorLockMode value) => _lockState = value;
}
