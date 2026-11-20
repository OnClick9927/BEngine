using BEngine.Platform;

namespace BEngine;

/// <summary>Unified API backed by the active platform service provider.</summary>
public static class Cursor
{
    public static bool visible { get => CursorUtility.Current.visible; set => CursorUtility.Current.visible = value; }

    public static CursorLockMode lockState { get => CursorUtility.Current.lockState; set => CursorUtility.Current.lockState = value; }

    public static BAsset? texture => CursorUtility.Current.texture;

    public static Vector2 hotspot => CursorUtility.Current.hotspot;

    public static CursorMode mode => CursorUtility.Current.mode;

    public static void SetCursor(BAsset? cursorTexture, Vector2 cursorHotspot, CursorMode cursorMode) =>
        CursorUtility.Current.SetCursor(cursorTexture, cursorHotspot, cursorMode);
    internal static CursorLockMode GetLockStateUnchecked() => lockState;
    internal static void SetLockStateUnchecked(CursorLockMode value) => lockState = value;
}
