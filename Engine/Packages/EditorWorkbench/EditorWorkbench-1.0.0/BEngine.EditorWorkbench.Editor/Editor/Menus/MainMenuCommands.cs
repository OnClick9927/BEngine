namespace BEngine.Editor;

internal static class MainMenuCommands
{
    [MenuItem("File/New Scene", false, 0)]
    private static void NewScene() => Execute(MainMenuCommand.NewScene);

    [MenuItem("File/New Scene", true)]
    private static bool ValidateNewScene() => CanExecute(MainMenuCommand.NewScene);

    [MenuItem("File/Open Scene...", false, 10)]
    private static void OpenScene() => Execute(MainMenuCommand.OpenScene);

    [MenuItem("File/Open Scene...", true)]
    private static bool ValidateOpenScene() => CanExecute(MainMenuCommand.OpenScene);

    [MenuItem("File/Open Scene Additive...", false, 11)]
    private static void OpenSceneAdditive() => Execute(MainMenuCommand.OpenSceneAdditive);

    [MenuItem("File/Open Scene Additive...", true)]
    private static bool ValidateOpenSceneAdditive() => CanExecute(MainMenuCommand.OpenSceneAdditive);

    [MenuItem("File/Save Scene", false, 100)]
    private static void SaveScene() => Execute(MainMenuCommand.SaveScene);

    [MenuItem("File/Save Scene", true)]
    private static bool ValidateSaveScene() => CanExecute(MainMenuCommand.SaveScene);

    [MenuItem("File/Save All Scenes", false, 101)]
    private static void SaveAllScenes() => Execute(MainMenuCommand.SaveAllScenes);

    [MenuItem("File/Save All Scenes", true)]
    private static bool ValidateSaveAllScenes() => CanExecute(MainMenuCommand.SaveAllScenes);

    [MenuItem("File/Show Project in Explorer", false, 500)]
    private static void ShowProjectInExplorer() => Execute(MainMenuCommand.ShowProjectInExplorer);

    [MenuItem("File/Show Project in Explorer", true)]
    private static bool ValidateShowProjectInExplorer() => CanExecute(MainMenuCommand.ShowProjectInExplorer);

    [MenuItem("File/Exit", false, 1000)]
    private static void Exit() => Execute(MainMenuCommand.Exit);

    [MenuItem("File/Exit", true)]
    private static bool ValidateExit() => CanExecute(MainMenuCommand.Exit);

    [MenuItem("Edit/Undo", false, 0)]
    private static void Undo() => Execute(MainMenuCommand.Undo);

    [MenuItem("Edit/Undo", true)]
    private static bool ValidateUndo() => CanExecute(MainMenuCommand.Undo);

    [MenuItem("Edit/Redo", false, 1)]
    private static void Redo() => Execute(MainMenuCommand.Redo);

    [MenuItem("Edit/Redo", true)]
    private static bool ValidateRedo() => CanExecute(MainMenuCommand.Redo);

    [MenuItem("Edit/Copy", false, 100)]
    private static void Copy() => Execute(MainMenuCommand.Copy);

    [MenuItem("Edit/Copy", true)]
    private static bool ValidateCopy() => CanExecute(MainMenuCommand.Copy);

    [MenuItem("Edit/Paste", false, 101)]
    private static void Paste() => Execute(MainMenuCommand.Paste);

    [MenuItem("Edit/Paste", true)]
    private static bool ValidatePaste() => CanExecute(MainMenuCommand.Paste);

    [MenuItem("Edit/Duplicate", false, 102)]
    private static void Duplicate() => Execute(MainMenuCommand.Duplicate);

    [MenuItem("Edit/Duplicate", true)]
    private static bool ValidateDuplicate() => CanExecute(MainMenuCommand.Duplicate);

    [MenuItem("Edit/Rename", false, 110)]
    private static void Rename() => Execute(MainMenuCommand.Rename);

    [MenuItem("Edit/Rename", true)]
    private static bool ValidateRename() => CanExecute(MainMenuCommand.Rename);

    [MenuItem("Edit/Delete", false, 111)]
    private static void Delete() => Execute(MainMenuCommand.Delete);

    [MenuItem("Edit/Delete", true)]
    private static bool ValidateDelete() => CanExecute(MainMenuCommand.Delete);

    [MenuItem("Edit/Select All", false, 200)]
    private static void SelectAll() => Execute(MainMenuCommand.SelectAll);

    [MenuItem("Edit/Select All", true)]
    private static bool ValidateSelectAll() => CanExecute(MainMenuCommand.SelectAll);

    [MenuItem("Edit/Deselect All", false, 201)]
    private static void DeselectAll() => Execute(MainMenuCommand.DeselectAll);

    [MenuItem("Edit/Deselect All", true)]
    private static bool ValidateDeselectAll() => CanExecute(MainMenuCommand.DeselectAll);

    [MenuItem("Edit/Frame Selected", false, 210)]
    private static void FrameSelected() => Execute(MainMenuCommand.FrameSelected);

    [MenuItem("Edit/Frame Selected", true)]
    private static bool ValidateFrameSelected() => CanExecute(MainMenuCommand.FrameSelected);

    [MenuItem("Edit/Play", false, 300)]
    private static void Play() => Execute(MainMenuCommand.Play);

    [MenuItem("Edit/Play", true)]
    private static bool ValidatePlay() => ValidateChecked(MainMenuCommand.Play, "Edit/Play");

    [MenuItem("Edit/Pause", false, 301)]
    private static void Pause() => Execute(MainMenuCommand.Pause);

    [MenuItem("Edit/Pause", true)]
    private static bool ValidatePause() => ValidateChecked(MainMenuCommand.Pause, "Edit/Pause");

    [MenuItem("Edit/Step", false, 302)]
    private static void Step() => Execute(MainMenuCommand.Step);

    [MenuItem("Edit/Step", true)]
    private static bool ValidateStep() => CanExecute(MainMenuCommand.Step);

    [MenuItem("Edit/Recompile Scripts", false, 400)]
    private static void RecompileScripts() => Execute(MainMenuCommand.RecompileScripts);

    [MenuItem("Edit/Recompile Scripts", true)]
    private static bool ValidateRecompileScripts() => CanExecute(MainMenuCommand.RecompileScripts);

    [MenuItem("Window/Panels/Close Focused Tab", false, 500)]
    private static void CloseFocusedWindow() => Execute(MainMenuCommand.CloseFocusedWindow);

    [MenuItem("Window/Panels/Close Focused Tab", true)]
    private static bool ValidateCloseFocusedWindow() => CanExecute(MainMenuCommand.CloseFocusedWindow);

    [MenuItem("Window/Panels/Lock Focused Window", false, 501)]
    private static void ToggleLockFocusedWindow() => Execute(MainMenuCommand.ToggleLockFocusedWindow);

    [MenuItem("Window/Panels/Lock Focused Window", true)]
    private static bool ValidateToggleLockFocusedWindow() =>
        ValidateChecked(MainMenuCommand.ToggleLockFocusedWindow, "Window/Panels/Lock Focused Window");

    [MenuItem("Window/Panels/Maximize Focused Tab", false, 502)]
    private static void ToggleMaximizeFocusedWindow() => Execute(MainMenuCommand.ToggleMaximizeFocusedWindow);

    [MenuItem("Window/Panels/Maximize Focused Tab", true)]
    private static bool ValidateToggleMaximizeFocusedWindow() =>
        ValidateChecked(MainMenuCommand.ToggleMaximizeFocusedWindow, "Window/Panels/Maximize Focused Tab");

    [MenuItem("Window/Panels/Next Window", false, 510)]
    private static void NextWindow() => Execute(MainMenuCommand.NextWindow);

    [MenuItem("Window/Panels/Next Window", true)]
    private static bool ValidateNextWindow() => CanExecute(MainMenuCommand.NextWindow);

    [MenuItem("Window/Panels/Previous Window", false, 511)]
    private static void PreviousWindow() => Execute(MainMenuCommand.PreviousWindow);

    [MenuItem("Window/Panels/Previous Window", true)]
    private static bool ValidatePreviousWindow() => CanExecute(MainMenuCommand.PreviousWindow);

    [MenuItem("Help/Documentation", false, 100)]
    private static void Documentation() => Execute(MainMenuCommand.Documentation);

    [MenuItem("Help/Documentation", true)]
    private static bool ValidateDocumentation() => CanExecute(MainMenuCommand.Documentation);

    [MenuItem("Help/View Editor Log", false, 110)]
    private static void ViewEditorLog() => Execute(MainMenuCommand.ViewEditorLog);

    [MenuItem("Help/View Editor Log", true)]
    private static bool ValidateViewEditorLog() => CanExecute(MainMenuCommand.ViewEditorLog);

    [MenuItem("Help/Reveal Logs Folder", false, 111)]
    private static void RevealLogsFolder() => Execute(MainMenuCommand.RevealLogsFolder);

    [MenuItem("Help/Reveal Logs Folder", true)]
    private static bool ValidateRevealLogsFolder() => CanExecute(MainMenuCommand.RevealLogsFolder);

    [MenuItem("Help/Copy System Info", false, 120)]
    private static void CopySystemInfo() => Execute(MainMenuCommand.CopySystemInfo);

    [MenuItem("Help/About BEngine", false, 1000)]
    private static void About() => Execute(MainMenuCommand.About);

    private static bool ValidateChecked(MainMenuCommand command, string menuPath)
    {
        Menu.SetChecked(menuPath, EditorBridge.Host?.IsMainMenuCommandChecked(command) == true);
        return CanExecute(command);
    }

    private static bool CanExecute(MainMenuCommand command) =>
        EditorBridge.Host?.CanExecuteMainMenuCommand(command) == true;

    private static void Execute(MainMenuCommand command) =>
        EditorBridge.Host?.ExecuteMainMenuCommand(command);
}
