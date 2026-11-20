namespace BEngine.Editor;

public static class EditorResource
{
    public const string DirectoryName = "Editor";
    private static readonly ResourceFolder Folder = new(DirectoryName);

    public static T? Load<T>(string path) where T : class =>
        Folder.Load<T>(path);

    public static object? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return Folder.Load(path, type);
    }

    public static string? FindPath(string path) => Folder.FindPath(path);

    public static void RegisterResourceRoot(string rootPath) => Resources.RegisterResourceRoot(rootPath);
}
