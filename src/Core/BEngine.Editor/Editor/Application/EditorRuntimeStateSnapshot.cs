namespace BEngine.Editor;

internal readonly record struct EditorRuntimeStateSnapshot(
    Fix64 DeltaTime,
    Fix64 UnscaledDeltaTime,
    Fix64 FixedDeltaTime,
    Fix64 TimeValue,
    Fix64 UnscaledTime,
    Fix64 FixedTime,
    Fix64 TimeScale,
    Fix64 MaximumDeltaTime,
    bool InFixedTimeStep,
    long FrameCount,
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
        Time.deltaTime,
        Time.unscaledDeltaTime,
        Time.fixedDeltaTime,
        Time.time,
        Time.unscaledTime,
        Time.fixedTime,
        Time.timeScale,
        Time.maximumDeltaTime,
        Time.inFixedTimeStep,
        Time.frameCount,
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
        Time.deltaTime = DeltaTime;
        Time.unscaledDeltaTime = UnscaledDeltaTime;
        Time.fixedDeltaTime = FixedDeltaTime;
        Time.time = TimeValue;
        Time.unscaledTime = UnscaledTime;
        Time.fixedTime = FixedTime;
        Time.timeScale = TimeScale;
        Time.maximumDeltaTime = MaximumDeltaTime;
        Time.inFixedTimeStep = InFixedTimeStep;
        Time.frameCount = FrameCount;
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
