namespace BEngine.Editor;

internal readonly record struct EditorRuntimeStateSnapshot(
    Time.State Clock,
    int TargetFrameRate,
    int QualityLevel,
    int VSyncCount,
    int AntiAliasing,
    bool RealtimeReflectionProbes,
    Fix64 ShadowDistance,
    Resolution Resolution,
    FullScreenMode FullScreenMode,
    bool CursorVisible,
    CursorLockMode CursorLockState,
    BAsset? CursorTexture,
    Vector2 CursorHotspot,
    CursorMode CursorMode,
    BEngine.Random.State RandomState)
{
    internal static EditorRuntimeStateSnapshot Capture() => new(
        Time.CaptureState(),
        Application.targetFrameRate,
        QualitySettings.qualityLevel,
        QualitySettings.vSyncCount,
        QualitySettings.antiAliasing,
        QualitySettings.realtimeReflectionProbes,
        QualitySettings.shadowDistance,
        Screen.currentResolution,
        Screen.fullScreenMode,
        Cursor.visible,
        Cursor.lockState,
        Cursor.texture,
        Cursor.hotspot,
        Cursor.mode,
        BEngine.Random.state);

    internal void Restore()
    {
        Time.RestoreState(Clock);
        Application.targetFrameRate = TargetFrameRate;
        QualitySettings.SetQualityLevel(QualityLevel);
        QualitySettings.vSyncCount = VSyncCount;
        QualitySettings.antiAliasing = AntiAliasing;
        QualitySettings.realtimeReflectionProbes = RealtimeReflectionProbes;
        QualitySettings.shadowDistance = ShadowDistance;
        Screen.SetResolution(Resolution.width, Resolution.height, FullScreenMode, Resolution.refreshRate);
        Cursor.visible = CursorVisible;
        Cursor.lockState = CursorLockState;
        Cursor.SetCursor(CursorTexture, CursorHotspot, CursorMode);
        BEngine.Random.state = RandomState;
    }
}
