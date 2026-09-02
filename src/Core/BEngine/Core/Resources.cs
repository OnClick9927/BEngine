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

    public static ResourceRequest<T> LoadAsync<T>(string path) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ResourceRequest<T>(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Load<T>(path);
        });
    }

    public static ResourceRequest LoadAsync(string path, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(type);
        return new ResourceRequest(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Load(path, type);
        });
    }

    public static ResourceHandle<T> Acquire<T>(string path) where T : class =>
        ResourceLeaseRegistry.Acquire<T>(path, "Resources");

    public static int GetReferenceCount(object asset) => ResourceLeaseRegistry.GetReferenceCount(asset);

    public static void RegisterResourceRoot(string rootPath) => ResourceLoader.RegisterRoot(rootPath);
    public static bool UnregisterResourceRoot(string rootPath) => ResourceLoader.UnregisterRoot(rootPath);
    public static void RegisterResourceProvider(IResourceProvider provider) => ResourceLoader.RegisterProvider(provider);
    public static bool UnregisterResourceProvider(IResourceProvider provider) => ResourceLoader.UnregisterProvider(provider);
    public static T[] LoadAll<T>(string path = "") where T : class =>
        ResourceLoader.LoadAll<T>(path, "Resources");

    public static void UnloadAsset(BObject assetToUnload)
    {
        ArgumentNullException.ThrowIfNull(assetToUnload);
        BAssetReferenceLoader.Unload(assetToUnload);
        BObject.DestroyImmediate(assetToUnload);
    }

    public static AsyncOperation UnloadUnusedAssets() =>
        new ResourceRequest(cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ResourceLeaseRegistry.UnloadUnused();
            BAssetReferenceLoader.PruneMissingFiles();
            return null;
        });
}
