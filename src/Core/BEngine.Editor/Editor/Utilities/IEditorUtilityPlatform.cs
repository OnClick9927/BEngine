namespace BEngine.Editor;

internal interface IEditorUtilityPlatform
{
    int DisplayDialog(string title, string message, IReadOnlyList<string> buttons);
    string OpenFilePanel(string title, string directory, string filter);
    string OpenFolderPanel(string title, string folder, string defaultName);
    void OpenWithDefaultApp(string target);
    void ShowProgress(EditorProgressInfo progress, Action? requestCancellation);
    void ClearProgress();
}
