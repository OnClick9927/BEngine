
namespace BEngine.Editor;

public static class PrefabStageUtility
{
    public static PrefabStage? GetCurrentPrefabStage() => EditorBridge.Host?.CurrentPrefabStage;
    public static PrefabStage? GetPrefabStage(GameObject gameObject)
    {
        var stage = GetCurrentPrefabStage();
        return stage is not null && ReferenceEquals(gameObject?.scene, stage.scene) ? stage : null;
    }

    public static PrefabStage? OpenPrefab(string assetPath) =>
        EditorBridge.Host?.OpenPrefabStage(assetPath) is true ? GetCurrentPrefabStage() : null;
}
