namespace BEngine.Platform;

public sealed class PlatformCursorService : ICursorService
{
    private bool _visible = true;
    private CursorLockMode _lockState;
    private BAsset? _texture;
    private Vector2 _hotspot;
    private CursorMode _mode;

    public bool visible
    {
        get { return _visible; }
        set { _visible = value; }
    }
    public CursorLockMode lockState
    {
        get { return _lockState; }
        set { _lockState = value; }
    }
    public BAsset? texture
    {
        get { return _texture; }
    }
    public Vector2 hotspot
    {
        get { return _hotspot; }
    }
    public CursorMode mode
    {
        get { return _mode; }
    }

    public void SetCursor(BAsset? cursorTexture, Vector2 cursorHotspot, CursorMode cursorMode)
    {
        _texture = cursorTexture;
        _hotspot = cursorHotspot;
        _mode = cursorMode;
    }

    internal void SetLockStateUnchecked(CursorLockMode value) => _lockState = value;
}
