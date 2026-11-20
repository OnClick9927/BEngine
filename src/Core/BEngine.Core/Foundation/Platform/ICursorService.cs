namespace BEngine.Platform;

/// <summary>Platform cursor SPI. Consumers use Cursor or CursorUtility.</summary>
public interface ICursorService
{
    bool visible { get; set; }
    CursorLockMode lockState { get; set; }
    BAsset? texture { get; }
    Vector2 hotspot { get; }
    CursorMode mode { get; }
    void SetCursor(BAsset? cursorTexture, Vector2 cursorHotspot, CursorMode cursorMode);
}
