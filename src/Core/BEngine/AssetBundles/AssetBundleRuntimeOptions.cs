namespace BEngine.AssetBundles;

public sealed class AssetBundleRuntimeOptions
{
    public string PackageName { get; init; } = string.Empty;
    public string CacheDirectory { get; init; } = string.Empty;
    public string? BuiltInDirectory { get; init; }
    public Uri? RemoteBaseUri { get; init; }
    public int MaxRetries { get; init; } = 3;
    public int MaxConcurrentDownloads { get; init; } = 4;
    public HttpClient? HttpClient { get; init; }
    public bool RequireHttps { get; init; } = true;
    public int MaximumBundleCount { get; init; } = 10_000;
    public int MaximumAssetCount { get; init; } = 1_000_000;
    public long MaximumBundleSize { get; init; } = 2L * 1024 * 1024 * 1024;
    public long MaximumAssetSize { get; init; } = 512L * 1024 * 1024;
    public long MaximumCatalogSize { get; init; } = 16L * 1024 * 1024;

    internal AssetBundleRuntimeOptions Validate()
    {
        if (string.IsNullOrWhiteSpace(PackageName))
            throw new ArgumentException("Asset bundle package name is required.", nameof(PackageName));
        if (string.IsNullOrWhiteSpace(CacheDirectory))
            throw new ArgumentException("Asset bundle cache directory is required.", nameof(CacheDirectory));
        if (MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetries));
        if (MaxConcurrentDownloads <= 0) throw new ArgumentOutOfRangeException(nameof(MaxConcurrentDownloads));
        if (MaximumBundleCount <= 0 || MaximumAssetCount <= 0 || MaximumBundleSize <= 0 ||
            MaximumAssetSize < 0 || MaximumCatalogSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumBundleSize), "Asset bundle limits must be positive.");
        if (RemoteBaseUri is not null)
        {
            if (!RemoteBaseUri.IsAbsoluteUri)
                throw new ArgumentException("Remote base URI must be absolute.", nameof(RemoteBaseUri));
            if (RequireHttps && !RemoteBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Remote asset bundles require HTTPS.", nameof(RemoteBaseUri));
            if (RemoteBaseUri.Scheme is not ("http" or "https"))
                throw new ArgumentException("Remote asset bundles require an HTTP or HTTPS URI.", nameof(RemoteBaseUri));
        }
        return this;
    }
}
