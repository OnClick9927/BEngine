using BEngine.Networking;
using BEngine.Serialization;

namespace BEngine.AssetBundles;

public sealed class AssetBundleRuntimeOptions
{
    public const string StreamingAssetsUrlEnvironmentVariable = "BENGINE_STREAMING_ASSETS_URL";
    public BEngine.Content.ContentEncryptionKey? EncryptionKey { get; init; }
    /// <summary>Rejects plaintext bundles when a Player was built with encryption.</summary>
    public bool RequireEncryptedContent { get; init; }
    public string PackageName { get; init; } = string.Empty;
    public string CacheDirectory { get; init; } = string.Empty;
    public string? BuiltInDirectory { get; init; }
    /// <summary>HTTP directory containing a packaged release. Read on demand into memory, without local storage.</summary>
    public Uri? BuiltInBaseUri { get; init; }
    public Uri? RemoteBaseUri { get; init; }
    /// <summary>False loads the built-in release in place without cache directories or activation pointers.</summary>
    public bool UsePersistentCache { get; init; } = true;
    /// <summary>Null installs every bundle. Otherwise installs these roots and their dependencies.
    /// Managed code and package resources are always required.</summary>
    public IReadOnlyList<string>? RequiredBundleNames { get; init; }
    public int MaxRetries { get; init; } = 3;
    /// <summary>Timeout without HTTP response progress; active transfers may take longer.</summary>
    public TimeSpan HttpInactivityTimeout { get; init; } = TimeSpan.FromSeconds(60);
    public IHttpTransport? HttpTransport { get; init; }
    public Action<AssetBundleHttpTransferDiagnostic>? HttpTransferObserver { get; init; }
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
        if (UsePersistentCache && string.IsNullOrWhiteSpace(CacheDirectory))
            throw new ArgumentException("Asset bundle cache directory is required.", nameof(CacheDirectory));
        if (!UsePersistentCache)
        {
            if (string.IsNullOrWhiteSpace(BuiltInDirectory) && BuiltInBaseUri is null)
                throw new ArgumentException(
                    "Persistent-cache-free AssetBundles require a built-in directory or HTTP URI.",
                    nameof(BuiltInDirectory));
            if (RemoteBaseUri is not null)
                throw new ArgumentException(
                    "Persistent-cache-free AssetBundles cannot use a remote source.",
                    nameof(RemoteBaseUri));
        }
        if (BuiltInBaseUri is not null)
        {
            if (UsePersistentCache || !string.IsNullOrWhiteSpace(BuiltInDirectory))
                throw new ArgumentException("A built-in HTTP source requires read-only mode and no file source.", nameof(BuiltInBaseUri));
            if (!BuiltInBaseUri.IsAbsoluteUri || BuiltInBaseUri.Scheme is not ("http" or "https") ||
                BuiltInBaseUri.Query.Length != 0 || BuiltInBaseUri.Fragment.Length != 0 ||
                BuiltInBaseUri.UserInfo.Length != 0 || !BuiltInBaseUri.AbsolutePath.EndsWith('/'))
                throw new ArgumentException("The built-in HTTP URI must be an absolute HTTP(S) directory without credentials, query or fragment.", nameof(BuiltInBaseUri));
        }
        if (MaxRetries < 0) throw new ArgumentOutOfRangeException(nameof(MaxRetries));
        if (HttpInactivityTimeout <= TimeSpan.Zero && HttpInactivityTimeout != HttpNetworkRequest.InfiniteTimeout)
            throw new ArgumentOutOfRangeException(nameof(HttpInactivityTimeout));
        if (RequiredBundleNames?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Required bundle names cannot be empty.", nameof(RequiredBundleNames));
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

    /// <summary>Creates a read-only configuration. Web reads the site's StreamingAssets over HTTP on demand.</summary>
    public static AssetBundleRuntimeOptions FromStreamingAssets(
        string packageName, string relativeDirectory = "AssetBundles")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(relativeDirectory);
        AssetDataValidation.ValidateOptionalRelativePath(relativeDirectory, nameof(relativeDirectory));
        AssetDataValidation.ValidateOptionalRelativePath(packageName, nameof(packageName));
        var httpRoot = RuntimeServiceAccess.PlatformService.IsBrowser
            ? RuntimeServiceAccess.PlatformService.GetEnvironmentVariable(StreamingAssetsUrlEnvironmentVariable)
            : null;
        return new AssetBundleRuntimeOptions
        {
            PackageName = packageName,
            BuiltInBaseUri = string.IsNullOrWhiteSpace(httpRoot) ? null
                : ResolveStreamingAssetsUri(httpRoot, relativeDirectory, packageName),
            BuiltInDirectory = !string.IsNullOrWhiteSpace(httpRoot) ? null : RuntimeServiceAccess.PathService.Combine(
                Application.streamingAssetsPath, relativeDirectory, packageName),
            UsePersistentCache = false
        };
    }

    public static Uri ResolveStreamingAssetsUri(string streamingAssetsUrl, string relativeDirectory, string packageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamingAssetsUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        AssetDataValidation.ValidateOptionalRelativePath(relativeDirectory, nameof(relativeDirectory));
        AssetDataValidation.ValidateOptionalRelativePath(packageName, nameof(packageName));
        var root = new Uri(streamingAssetsUrl, UriKind.Absolute);
        if (root.Scheme is not ("http" or "https") || root.Query.Length != 0 || root.Fragment.Length != 0 ||
            root.UserInfo.Length != 0 || !root.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("StreamingAssets URL must be an HTTP(S) directory.", nameof(streamingAssetsUrl));
        var segments = (relativeDirectory.Replace('\\', '/').Trim('/') + "/" + packageName.Replace('\\', '/'))
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return new Uri(root, string.Join('/', segments.Select(Uri.EscapeDataString)) + "/");
    }
}
