namespace BEngine.Editor;

internal static class ComponentMenuCommands
{
    [MenuItem("Tools/Remove Missing Components", false, 2020)]
    private static void RemoveMissingComponents() => Execute(ComponentCommand.RemoveMissingComponents);

    [MenuItem("Tools/Remove Missing Components", true)]
    private static bool ValidateRemoveMissingComponents() => CanExecute(ComponentCommand.RemoveMissingComponents);

    private static bool CanExecute(ComponentCommand command) =>
        EditorBridge.Host?.CanExecuteComponentCommand(command) == true;

    private static void Execute(ComponentCommand command) =>
        EditorBridge.Host?.ExecuteComponentCommand(command);
}
