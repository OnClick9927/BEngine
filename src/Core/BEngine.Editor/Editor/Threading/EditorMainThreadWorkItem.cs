namespace BEngine.Editor;

internal readonly record struct EditorMainThreadWorkItem(string Name, Action Callback);
