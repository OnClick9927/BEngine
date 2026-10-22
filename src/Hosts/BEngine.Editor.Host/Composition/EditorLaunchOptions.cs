namespace BEngine.Editor;

public sealed record EditorLaunchOptions(
    string ProjectPath,
    bool OpenEditorStatusOnStart = false,
    bool OpenUiBuilderOnStart = false);
