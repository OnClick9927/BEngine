namespace BEngine.Platform;

/// <summary>Platform render settings SPI. Consumers use QualitySettings or RenderSettingsUtility.</summary>
public interface IRenderSettingsService
{
    int targetFrameRate { get; set; }
    /// <summary>
    /// Resolution multiplier used for post-process intermediate targets. A value of
    /// one keeps native resolution; lower values reduce raster work before the
    /// final filtered composite. Platform hosts quantize and clamp the value.
    /// </summary>
    Fix64 renderScale { get; set; }
    int qualityLevel { get; }
    string[] names { get; }
    int vSyncCount { get; set; }
    int antiAliasing { get; set; }
    bool realtimeReflectionProbes { get; set; }
    Fix64 shadowDistance { get; set; }
    void SetQualityLevel(int index, bool applyExpensiveChanges = true);
    void IncreaseLevel(bool applyExpensiveChanges = false);
    void DecreaseLevel(bool applyExpensiveChanges = false);
}
