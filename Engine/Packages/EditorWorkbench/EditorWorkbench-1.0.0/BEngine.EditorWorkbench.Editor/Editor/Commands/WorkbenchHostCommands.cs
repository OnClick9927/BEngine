namespace BEngine.Editor;

internal static class WorkbenchHostCommands
{
    internal static bool CanExecuteProjectAssetCommand(this IEditorHost host, ProjectAssetCommand command) => host.CanExecuteCommand(EditorCommandId.From(command), null);
    internal static bool ExecuteProjectAssetCommand(this IEditorHost host, ProjectAssetCommand command) => host.ExecuteCommand(EditorCommandId.From(command), null);
    internal static bool CanExecuteGameObjectCommand(this IEditorHost host, GameObjectCommand command, GameObject? target) => host.CanExecuteCommand(EditorCommandId.From(command), target);
    internal static bool ExecuteGameObjectCommand(this IEditorHost host, GameObjectCommand command, GameObject? target) => host.ExecuteCommand(EditorCommandId.From(command), target);
    internal static bool CanExecuteComponentCommand(this IEditorHost host, ComponentCommand command) => host.CanExecuteCommand(EditorCommandId.From(command), null);
    internal static bool ExecuteComponentCommand(this IEditorHost host, ComponentCommand command) => host.ExecuteCommand(EditorCommandId.From(command), null);
    internal static bool CanExecuteCameraViewCommand(this IEditorHost host, CameraViewCommand command, Component camera) => host.CanExecuteCommand(EditorCommandId.From(command), camera);
    internal static bool ExecuteCameraViewCommand(this IEditorHost host, CameraViewCommand command, Component camera) => host.ExecuteCommand(EditorCommandId.From(command), camera);
    internal static bool CanExecuteMainMenuCommand(this IEditorHost host, MainMenuCommand command) => host.CanExecuteCommand(EditorCommandId.From(command), null);
    internal static bool ExecuteMainMenuCommand(this IEditorHost host, MainMenuCommand command) => host.ExecuteCommand(EditorCommandId.From(command), null);
    internal static bool IsMainMenuCommandChecked(this IEditorHost host, MainMenuCommand command) => host.IsCommandChecked(EditorCommandId.From(command));
}
