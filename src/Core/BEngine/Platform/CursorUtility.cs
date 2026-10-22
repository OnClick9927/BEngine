namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies ICursorService through dependency injection.</summary>
public sealed class CursorUtility
{
    private readonly ICursorService _implementation;

    public CursorUtility(ICursorService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static CursorUtility Current => RuntimeServiceAccess.GetRequiredService<CursorUtility>();

    public bool visible { get => _implementation.visible; set => _implementation.visible = value; }

    public CursorLockMode lockState { get => _implementation.lockState; set => _implementation.lockState = value; }

    public BAsset? texture => _implementation.texture;

    public Vector2 hotspot => _implementation.hotspot;

    public CursorMode mode => _implementation.mode;

    public void SetCursor(BAsset? cursorTexture, Vector2 cursorHotspot, CursorMode cursorMode) =>
        _implementation.SetCursor(cursorTexture, cursorHotspot, cursorMode);
}
