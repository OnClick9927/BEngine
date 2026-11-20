namespace BEngine.Editor;

internal static class CameraComponentMenuCommands
{
    [MenuItem("CONTEXT/Camera2D/Align With View", false, 100)]
    private static void AlignWithView(MenuCommand command) =>
        Execute(CameraViewCommand.AlignWithView, command);

    [MenuItem("CONTEXT/Camera2D/Align With View", true)]
    private static bool ValidateAlignWithView(MenuCommand command) =>
        CanExecute(CameraViewCommand.AlignWithView, command);

    [MenuItem("CONTEXT/Camera2D/Move To View", false, 101)]
    private static void MoveToView(MenuCommand command) =>
        Execute(CameraViewCommand.MoveToView, command);

    [MenuItem("CONTEXT/Camera2D/Move To View", true)]
    private static bool ValidateMoveToView(MenuCommand command) =>
        CanExecute(CameraViewCommand.MoveToView, command);

    private static Camera2D? Target(MenuCommand command) => command.context as Camera2D;

    private static bool CanExecute(CameraViewCommand command, MenuCommand menuCommand) =>
        Target(menuCommand) is { } camera &&
        EditorBridge.Host?.CanExecuteCameraViewCommand(command, camera) == true;

    private static void Execute(CameraViewCommand command, MenuCommand menuCommand)
    {
        if (Target(menuCommand) is { } camera)
            EditorBridge.Host?.ExecuteCameraViewCommand(command, camera);
    }
}
