namespace BEngine.Editor;

internal static class ComponentMenuCommands
{
    [MenuItem("Component/Enable All Components", false, 2000)]
    private static void EnableAllComponents() => Execute(ComponentCommand.EnableAllComponents);

    [MenuItem("Component/Enable All Components", true)]
    private static bool ValidateEnableAllComponents() => CanExecute(ComponentCommand.EnableAllComponents);

    [MenuItem("Component/Disable All Components", false, 2001)]
    private static void DisableAllComponents() => Execute(ComponentCommand.DisableAllComponents);

    [MenuItem("Component/Disable All Components", true)]
    private static bool ValidateDisableAllComponents() => CanExecute(ComponentCommand.DisableAllComponents);

    [MenuItem("Component/Reset All Components", false, 2010)]
    private static void ResetAllComponents() => Execute(ComponentCommand.ResetAllComponents);

    [MenuItem("Component/Reset All Components", true)]
    private static bool ValidateResetAllComponents() => CanExecute(ComponentCommand.ResetAllComponents);

    [MenuItem("Component/Remove Missing Scripts", false, 2020)]
    private static void RemoveMissingScripts() => Execute(ComponentCommand.RemoveMissingScripts);

    [MenuItem("Component/Remove Missing Scripts", true)]
    private static bool ValidateRemoveMissingScripts() => CanExecute(ComponentCommand.RemoveMissingScripts);

    private static bool CanExecute(ComponentCommand command) =>
        EditorBridge.Host?.CanExecuteComponentCommand(command) == true;

    private static void Execute(ComponentCommand command) =>
        EditorBridge.Host?.ExecuteComponentCommand(command);
}
