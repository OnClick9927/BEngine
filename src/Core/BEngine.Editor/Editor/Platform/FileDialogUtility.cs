namespace BEngine.Editor;

/// <summary>Application-facing API; the platform host supplies IFileDialogService through dependency injection.</summary>
public sealed class FileDialogUtility
{
    private readonly IFileDialogService _implementation;

    public FileDialogUtility(IFileDialogService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static FileDialogUtility Current => EditorServices.GetRequiredService<FileDialogUtility>();

    public string? OpenFile(string title, string initialDirectory, string filter) =>
        _implementation.OpenFile(title, initialDirectory, filter);

    public string? SaveFile(string title, string initialDirectory, string filter, string defaultName) =>
        _implementation.SaveFile(title, initialDirectory, filter, defaultName);

    public string? OpenFolder(string title, string initialDirectory) =>
        _implementation.OpenFolder(title, initialDirectory);
}
