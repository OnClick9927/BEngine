using BEngine.Platform;

namespace BEngine;

public static class QualitySettings
{

    public static int qualityLevel => RenderSettingsUtility.Current.qualityLevel;

    public static string[] names => RenderSettingsUtility.Current.names;

    public static int vSyncCount { get => RenderSettingsUtility.Current.vSyncCount; set => RenderSettingsUtility.Current.vSyncCount = value; }

    public static Fix64 renderScale { get => RenderSettingsUtility.Current.renderScale; set => RenderSettingsUtility.Current.renderScale = value; }

    public static int antiAliasing { get => RenderSettingsUtility.Current.antiAliasing; set => RenderSettingsUtility.Current.antiAliasing = value; }

    public static bool realtimeReflectionProbes { get => RenderSettingsUtility.Current.realtimeReflectionProbes; set => RenderSettingsUtility.Current.realtimeReflectionProbes = value; }

    public static Fix64 shadowDistance { get => RenderSettingsUtility.Current.shadowDistance; set => RenderSettingsUtility.Current.shadowDistance = value; }

    public static void SetQualityLevel(int index, bool applyExpensiveChanges = true) =>
        RenderSettingsUtility.Current.SetQualityLevel(index, applyExpensiveChanges);

    public static void IncreaseLevel(bool applyExpensiveChanges = false) =>
        RenderSettingsUtility.Current.IncreaseLevel(applyExpensiveChanges);

    public static void DecreaseLevel(bool applyExpensiveChanges = false) =>
        RenderSettingsUtility.Current.DecreaseLevel(applyExpensiveChanges);
}
