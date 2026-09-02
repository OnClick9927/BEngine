namespace BEngine.Editor;

internal readonly record struct ComponentContextMenuCommand(
    string Name,
    string MethodName,
    Action<Component> Callback);
