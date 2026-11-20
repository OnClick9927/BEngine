using BEngine.Editor;

// This fixture exercises state changes, never native dialogs or GUI rendering.
public sealed class TestEditorUtilityPlatform : IEditorUtilityPlatform
{
    public int DisplayDialog(string title, string message, IReadOnlyList<string> buttons) => throw new NotSupportedException();
    public string OpenFilePanel(string title, string directory, string filter) => throw new NotSupportedException();
    public string OpenFolderPanel(string title, string folder, string defaultName) => throw new NotSupportedException();
    public void OpenWithDefaultApp(string target) => throw new NotSupportedException();
    public void ShowProgress(EditorProgressInfo progress, Action? requestCancellation) => throw new NotSupportedException();
    public void ClearProgress() => throw new NotSupportedException();
}
