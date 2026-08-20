
namespace BEngine.Editor;

public static class StageUtility
{
    public static PrefabStage? GetCurrentStageHandle() => PrefabStageUtility.GetCurrentPrefabStage();
    public static PrefabStage? GetMainStageHandle() => null;
    public static void GoToMainStage() => EditorBridge.Host?.ClosePrefabStage();
}
