using BEngine.Serialization;

namespace BEngine;

public static class QualitySettings
{
    private static readonly string[] Names = ["Very Low", "Low", "Medium", "High", "Very High", "Ultra"];
    public static int qualityLevel { get; private set; } = 3;
    public static string[] names => [.. Names];
    public static int vSyncCount { get; set; } = 1;
    public static int antiAliasing { get; set; }
    public static bool realtimeReflectionProbes { get; set; } = true;
    public static Fix64 shadowDistance { get; set; } = 50;
    public static void SetQualityLevel(int index, bool applyExpensiveChanges = true) =>
        qualityLevel = Math.Clamp(index, 0, Names.Length - 1);
    public static void IncreaseLevel(bool applyExpensiveChanges = false) => SetQualityLevel(qualityLevel + 1);
    public static void DecreaseLevel(bool applyExpensiveChanges = false) => SetQualityLevel(qualityLevel - 1);
}
