namespace BEngine.Editor;

public static class EditorFileDialog
{
    public static void Open(string title, string initialDirectory, string filter, Action<string> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (EditorFeatureGuard.TryInvoke("EditorFileDialog.Open", () =>
                WindowsNativeFileDialog.OpenFile(title, initialDirectory, filter), null, out var path) &&
            path is not null)
            EditorFeatureGuard.Invoke(typeof(EditorFileDialog), nameof(Open), () => accepted(path));
    }

    public static void Save(
        string title,
        string initialDirectory,
        string filter,
        string defaultName,
        Action<string> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (EditorFeatureGuard.TryInvoke("EditorFileDialog.Save", () =>
                WindowsNativeFileDialog.SaveFile(title, initialDirectory, filter, defaultName), null, out var path) &&
            path is not null)
            EditorFeatureGuard.Invoke(typeof(EditorFileDialog), nameof(Save), () => accepted(path));
    }

    public static void OpenFolder(string title, string initialDirectory, Action<string> accepted)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        if (EditorFeatureGuard.TryInvoke("EditorFileDialog.OpenFolder", () =>
                WindowsNativeFileDialog.OpenFolder(title, initialDirectory), null, out var path) && path is not null)
            EditorFeatureGuard.Invoke(typeof(EditorFileDialog), nameof(OpenFolder), () => accepted(path));
    }
}
