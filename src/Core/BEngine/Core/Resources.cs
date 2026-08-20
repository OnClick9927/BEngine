namespace BEngine;

public static class Resources
{
    public static T? Load<T>(string path) where T : class =>
        ResourceLoader.Load<T>(path, "Resources");

    public static object? Load(string path, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return ResourceLoader.Load(path, type, "Resources");
    }

    public static void RegisterResourceRoot(string rootPath) => ResourceLoader.RegisterRoot(rootPath);
    public static bool UnregisterResourceRoot(string rootPath) => ResourceLoader.UnregisterRoot(rootPath);
    public static void RegisterResourceProvider(IResourceProvider provider) => ResourceLoader.RegisterProvider(provider);
    public static bool UnregisterResourceProvider(IResourceProvider provider) => ResourceLoader.UnregisterProvider(provider);
    public static T[] LoadAll<T>(string path = "") where T : class =>
        ResourceLoader.LoadAll<T>(path, "Resources");

    public static void UnloadAsset(BObject assetToUnload)
    {
        if (assetToUnload is not null) BObject.DestroyImmediate(assetToUnload);
    }

    public static Task UnloadUnusedAssets()
    {
        GC.Collect();
        return Task.CompletedTask;
    }
}
