using BEngine.Editor;

namespace BEngine.Workbench;

public sealed partial class GpuEditorApplication
{
    private static readonly Dictionary<EditorCommandId, (int Group, int Value)> HostCommands = CreateHostCommands();
    private static Dictionary<EditorCommandId, (int, int)> CreateHostCommands()
    {
        var result = new Dictionary<EditorCommandId, (int, int)>();
        void Add<T>(int group) where T : struct, Enum
        { foreach (var value in Enum.GetValues<T>()) result.Add(EditorCommandId.From(value), (group, Convert.ToInt32(value))); }
        Add<ProjectAssetCommand>(0); Add<GameObjectCommand>(1); Add<ComponentCommand>(2); Add<CameraViewCommand>(3); Add<MainMenuCommand>(4);
        return result;
    }
    bool IEditorHost.CanExecuteCommand(EditorCommandId id, BObject? target)
    {
        if (!HostCommands.TryGetValue(id, out var command)) return false;
        return command.Group switch
        {
            0 => CurrentProjectWindow.CanExecuteCommand((ProjectAssetCommand)command.Value),
            1 => CanExecuteGameObjectCommand((GameObjectCommand)command.Value, target as GameObject),
            2 => CanExecuteComponentCommand((ComponentCommand)command.Value),
            3 => target is Camera2D camera && CanExecuteCameraViewCommand((CameraViewCommand)command.Value, camera),
            4 => CanExecuteMainMenuCommand((MainMenuCommand)command.Value),
            _ => false
        };
    }
    bool IEditorHost.ExecuteCommand(EditorCommandId id, BObject? target)
    {
        if (!HostCommands.TryGetValue(id, out var command)) return false;
        return command.Group switch
        {
            0 => CurrentProjectWindow.ExecuteCommand((ProjectAssetCommand)command.Value),
            1 => ExecuteGameObjectCommand((GameObjectCommand)command.Value, target as GameObject),
            2 => ExecuteComponentCommand((ComponentCommand)command.Value),
            3 => target is Camera2D camera && ExecuteCameraViewCommand((CameraViewCommand)command.Value, camera),
            4 => ExecuteMainMenuCommand((MainMenuCommand)command.Value),
            _ => false
        };
    }
    bool IEditorHost.IsCommandChecked(EditorCommandId id) => HostCommands.TryGetValue(id, out var command) &&
        command.Group == 4 && IsMainMenuCommandChecked((MainMenuCommand)command.Value);
}
