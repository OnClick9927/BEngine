namespace BEngine.Editor;
internal sealed class WindowsFileDialogService : IFileDialogService
{
    public string? OpenFile(string title, string initialDirectory, string filter) => WindowsNativeFileDialog.OpenFile(title, initialDirectory, filter);
    public string? SaveFile(string title, string initialDirectory, string filter, string defaultName) => WindowsNativeFileDialog.SaveFile(title, initialDirectory, filter, defaultName);
    public string? OpenFolder(string title, string initialDirectory) => WindowsNativeFileDialog.OpenFolder(title, initialDirectory);
}
