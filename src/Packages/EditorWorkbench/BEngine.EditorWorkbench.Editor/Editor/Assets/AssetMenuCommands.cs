namespace BEngine.Editor;

internal static class AssetMenuCommands
{
    [MenuItem("Assets/Open", false, 500)]
    private static void Open() => Execute(ProjectAssetCommand.Open);

    [MenuItem("Assets/Open", true)]
    private static bool ValidateOpen() => CanExecute(ProjectAssetCommand.Open);

    [MenuItem("Assets/Show in Explorer", false, 501)]
    private static void ShowInExplorer() => Execute(ProjectAssetCommand.ShowInExplorer);

    [MenuItem("Assets/Show in Explorer", true)]
    private static bool ValidateShowInExplorer() => CanExecute(ProjectAssetCommand.ShowInExplorer);

    [MenuItem("Assets/Open Scene/Additive", false, 510)]
    private static void OpenSceneAdditive() => OpenScene(OpenSceneMode.Additive);

    [MenuItem("Assets/Open Scene/Additive", true)]
    [MenuItem("Assets/Open Scene/Additive Without Loading", true)]
    private static bool ValidateOpenScene() => CanOpenScene();

    [MenuItem("Assets/Open Scene/Additive Without Loading", false, 511)]
    private static void OpenSceneAdditiveWithoutLoading() => OpenScene(OpenSceneMode.AdditiveWithoutLoading);

    [MenuItem("Assets/Copy Path", false, 520)]
    private static void CopyPath() => Execute(ProjectAssetCommand.CopyPath);

    [MenuItem("Assets/Copy Path", true)]
    private static bool ValidateCopyPath() => CanExecute(ProjectAssetCommand.CopyPath);

    [MenuItem("Assets/Copy Full Path", false, 521)]
    private static void CopyFullPath() => Execute(ProjectAssetCommand.CopyFullPath);

    [MenuItem("Assets/Copy Full Path", true)]
    private static bool ValidateCopyFullPath() => CanExecute(ProjectAssetCommand.CopyFullPath);

    [MenuItem("Assets/Rename", false, 540)]
    private static void Rename() => Execute(ProjectAssetCommand.Rename);

    [MenuItem("Assets/Rename", true)]
    private static bool ValidateRename() => CanExecute(ProjectAssetCommand.Rename);

    [MenuItem("Assets/Duplicate", false, 541)]
    private static void Duplicate() => Execute(ProjectAssetCommand.Duplicate);

    [MenuItem("Assets/Duplicate", true)]
    private static bool ValidateDuplicate() => CanExecute(ProjectAssetCommand.Duplicate);

    [MenuItem("Assets/Delete", false, 542)]
    private static void Delete() => Execute(ProjectAssetCommand.Delete);

    [MenuItem("Assets/Delete", true)]
    private static bool ValidateDelete() => CanExecute(ProjectAssetCommand.Delete);

    [MenuItem("Assets/Reimport", false, 560)]
    private static void Reimport() => Execute(ProjectAssetCommand.Reimport);

    [MenuItem("Assets/Reimport", true)]
    private static bool ValidateReimport() => CanExecute(ProjectAssetCommand.Reimport);

    [MenuItem("Assets/Refresh", false, 561)]
    private static void Refresh() => Execute(ProjectAssetCommand.Refresh);

    [MenuItem("Assets/Refresh", true)]
    private static bool ValidateRefresh() => CanExecute(ProjectAssetCommand.Refresh);

    [MenuItem("Assets/Import New Asset...", false, 1080)]
    private static void ImportNewAsset() => Execute(ProjectAssetCommand.ImportNewAsset);

    [MenuItem("Assets/Import New Asset...", true)]
    private static bool ValidateImportNewAsset() => CanExecute(ProjectAssetCommand.ImportNewAsset);

    private static bool CanOpenScene()
    {
        var host = EditorBridge.Host;
        if (host is null || host.IsPlaying || host.IsChangingPlayMode ||
            host.ActiveProjectAssetPath is not { } assetPath)
            return false;
        return host.GetAsset(assetPath) is { IsDirectory: false } asset &&
               asset.SourcePath.EndsWith(".scene.yaml", StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenScene(OpenSceneMode mode)
    {
        var host = EditorBridge.Host;
        if (!CanOpenScene() || host?.ActiveProjectAssetPath is not { } assetPath ||
            host.GetAsset(assetPath) is not { } asset)
            return;
        host.OpenScene(asset.SourcePath, mode);
    }

    private static bool CanExecute(ProjectAssetCommand command) =>
        EditorBridge.Host?.CanExecuteProjectAssetCommand(command) == true;

    private static void Execute(ProjectAssetCommand command) =>
        EditorBridge.Host?.ExecuteProjectAssetCommand(command);
}
