namespace BEngine;

public static class Resources
{
    /// <summary>Queries registered asset providers only, without falling back to source files.</summary>
    public static BAsset? LoadProvidedAsset(string path, Type assetType) => ResourceLoader.LoadAsset(path, assetType, "Resources");

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
        return new ResourceRequest<T>((AsyncLoadContext context) => ResourceLoader.LoadAsync(path, typeof(T), "Resources", context));
    }

    public static ResourceRequest LoadAsync(string path, Type type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(type);
        return new ResourceRequest((AsyncLoadContext context) => ResourceLoader.LoadAsync(path, type, "Resources", context));
    }

    /// <summary>Loads within a caller-owned preparation context, preserving its budget, progress and cancellation.</summary>
    public static BTask<object?> LoadAsync(string path, Type type, AsyncLoadContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(context);
        return ResourceLoader.LoadAsync(path, type, "Resources", context);
    }

    /// <summary>Prepares provider-owned data without creating or replacing the referenced object.</summary>
    public static BTask<bool> PrepareReferenceAsync(string path, AsyncLoadContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(context);
        return ResourceLoader.PrepareReferenceAsync(path, "Resources", context);
    }

    /// <summary>Loads and optionally prepares resources concurrently, with ordered results and owned leases.</summary>
    public static ResourceBatchRequest<T> LoadBatchAsync<T>(IEnumerable<string> paths,
        int maximumConcurrency = 4, Func<T, AsyncLoadContext, BValueTask>? prepare = null,
        BCancellationToken cancellationToken = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(paths);
        var values = paths.Take(65537).ToArray();
        if (values.Length > 65536) throw new ArgumentOutOfRangeException(nameof(paths));
        foreach (var path in values) ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ResourceBatchRequest<T>(values, maximumConcurrency, prepare, cancellationToken);
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
