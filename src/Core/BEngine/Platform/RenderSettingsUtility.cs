namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IRenderSettingsService through dependency injection.</summary>
public sealed class RenderSettingsUtility
{
    private readonly IRenderSettingsService _implementation;

    public RenderSettingsUtility(IRenderSettingsService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static RenderSettingsUtility Current => RuntimeServiceAccess.GetRequiredService<RenderSettingsUtility>();

    /// <summary>Gets the registered settings service without throwing in tooling and renderer tests.</summary>
    public static bool TryGetCurrent(out RenderSettingsUtility? settings)
    {
        settings = RuntimeServiceAccess.GetService<RenderSettingsUtility>();
        return settings is not null;
    }

    public int targetFrameRate { get => _implementation.targetFrameRate; set => _implementation.targetFrameRate = value; }

    public Fix64 renderScale { get => _implementation.renderScale; set => _implementation.renderScale = value; }

    public int qualityLevel => _implementation.qualityLevel;

    public string[] names => _implementation.names;

    public int vSyncCount { get => _implementation.vSyncCount; set => _implementation.vSyncCount = value; }

    public int antiAliasing { get => _implementation.antiAliasing; set => _implementation.antiAliasing = value; }

    public bool realtimeReflectionProbes { get => _implementation.realtimeReflectionProbes; set => _implementation.realtimeReflectionProbes = value; }

    public Fix64 shadowDistance { get => _implementation.shadowDistance; set => _implementation.shadowDistance = value; }

    public void SetQualityLevel(int index, bool applyExpensiveChanges = true) =>
        _implementation.SetQualityLevel(index, applyExpensiveChanges);

    public void IncreaseLevel(bool applyExpensiveChanges = false) =>
        _implementation.IncreaseLevel(applyExpensiveChanges);

    public void DecreaseLevel(bool applyExpensiveChanges = false) =>
        _implementation.DecreaseLevel(applyExpensiveChanges);
}
