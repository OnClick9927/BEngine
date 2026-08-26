namespace BEngine.Editor;

public static class EditorResource
{
    public const string DirectoryName = "Editor";

    public static T? Load<T>(string path) where T : class =>
        ResourceLoader.Load<T>(path, DirectoryName);

    public static object? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ResourceLoader.Load(path, type, DirectoryName);
    }

    public static string? FindPath(string path) => ResourceLoader.Resolve(path, DirectoryName);

    public static void RegisterResourceRoot(string rootPath) => Resources.RegisterResourceRoot(rootPath);
}
