namespace BEngine.Editor;
public interface IFileDialogService
{
    string? OpenFile(string title, string initialDirectory, string filter);
    string? SaveFile(string title, string initialDirectory, string filter, string defaultName);
    string? OpenFolder(string title, string initialDirectory);
}
