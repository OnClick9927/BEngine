using BEngine.Serialization;

namespace BEngine.Editor;

internal static class PrefabCommands
{
    [MenuItem("GameObject/Prefab/Save As Prefab Asset", false, 200)]
    private static void SaveAsPrefab()
    {
        var selected = Selection.activeGameObject;
        if (selected is null) return;
        var assets = EditorBridge.Host?.AssetsRootPath ?? Environment.CurrentDirectory;
        EditorFileDialog.Save("Save Prefab", assets, "BEngine Prefab|*.prefab.yaml",
            selected.name + ".prefab.yaml", path =>
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(selected, path, InteractionMode.UserAction);
                AssetDatabase.Refresh();
            });
    }
    [MenuItem("GameObject/Prefab/Save As Prefab Asset", true)]
    private static bool CanSaveAsPrefab() => Selection.activeGameObject is not null;

    [MenuItem("GameObject/Prefab/Open Prefab", false, 201)]
    private static void OpenPrefab()
    {
        var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(Selection.activeGameObject);
        if (!string.IsNullOrWhiteSpace(path)) PrefabStageUtility.OpenPrefab(path);
    }
    [MenuItem("GameObject/Prefab/Open Prefab", true)]
    private static bool CanOpenPrefab() => PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject);

    [MenuItem("GameObject/Prefab/Apply All", false, 202)]
    private static void Apply() => PrefabUtility.ApplyPrefabInstance(Selection.activeGameObject!);
    [MenuItem("GameObject/Prefab/Apply All", true)]
    private static bool CanApply() => PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject);

    [MenuItem("GameObject/Prefab/Revert All", false, 203)]
    private static void Revert() => PrefabUtility.RevertPrefabInstance(Selection.activeGameObject!);
    [MenuItem("GameObject/Prefab/Revert All", true)]
    private static bool CanRevert() => PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject);

    [MenuItem("GameObject/Prefab/Unpack", false, 204)]
    private static void Unpack() => PrefabUtility.UnpackPrefabInstance(Selection.activeGameObject!,
        PrefabUnpackMode.OutermostRoot);
    [MenuItem("GameObject/Prefab/Unpack", true)]
    private static bool CanUnpack() => PrefabUtility.IsPartOfPrefabInstance(Selection.activeGameObject);
}
