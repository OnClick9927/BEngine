namespace BEngine.Editor;

internal static class AssetMenuCommands
{
    private static readonly (string Label, ProjectAssetCommand Command)[] ContextCommands =
    [
        ("Open", ProjectAssetCommand.Open),
        ("Show in Explorer", ProjectAssetCommand.ShowInExplorer),
        ("Copy Path", ProjectAssetCommand.CopyPath),
        ("Copy Full Path", ProjectAssetCommand.CopyFullPath),
        ("Rename", ProjectAssetCommand.Rename),
        ("Duplicate", ProjectAssetCommand.Duplicate),
        ("Delete", ProjectAssetCommand.Delete),
        ("Reimport", ProjectAssetCommand.Reimport),
        ("Refresh", ProjectAssetCommand.Refresh)
    ];

    [MenuItem("Assets/Open", false, 500)]
    private static void Open() => Execute(ProjectAssetCommand.Open);

    [MenuItem("Assets/Open", true)]
    private static bool ValidateOpen() => CanExecute(ProjectAssetCommand.Open);

    [MenuItem("Assets/Show in Explorer", false, 501)]
    private static void ShowInExplorer() => Execute(ProjectAssetCommand.ShowInExplorer);

    [MenuItem("Assets/Show in Explorer", true)]
    private static bool ValidateShowInExplorer() => CanExecute(ProjectAssetCommand.ShowInExplorer);

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

    [MenuItem("Assets/Import New Asset...", false, 1080)]
    private static void ImportNewAsset() => Execute(ProjectAssetCommand.ImportNewAsset);

    [MenuItem("Assets/Import New Asset...", true)]
    private static bool ValidateImportNewAsset() => CanExecute(ProjectAssetCommand.ImportNewAsset);

    internal static void PopulateContextMenu(GenericMenu menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        foreach (var (label, command) in ContextCommands)
        {
            if (CanExecute(command))
                menu.AddItem(new GUIContent(label), false, () => Execute(command));
            else
                menu.AddDisabledItem(new GUIContent(label));
            if (command is ProjectAssetCommand.ShowInExplorer or ProjectAssetCommand.CopyFullPath or
                ProjectAssetCommand.Delete)
                menu.AddSeparator(string.Empty);
        }
    }

    private static bool CanExecute(ProjectAssetCommand command) =>
        EditorBridge.Host?.CanExecuteProjectAssetCommand(command) == true;

    private static void Execute(ProjectAssetCommand command) =>
        EditorBridge.Host?.ExecuteProjectAssetCommand(command);
}
