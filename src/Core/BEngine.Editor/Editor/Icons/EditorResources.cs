namespace BEngine.Editor;

public static class EditorResources
{
    public static T? Load<T>(string path) where T : class =>
        ResourceLoader.Load<T>(path, "EditorResources");

    public static object? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ResourceLoader.Load(path, type, "EditorResources");
    }

    public static string? FindPath(string path) => ResourceLoader.Resolve(path, "EditorResources");

    public static void RegisterResourceRoot(string rootPath) => Resources.RegisterResourceRoot(rootPath);

}
