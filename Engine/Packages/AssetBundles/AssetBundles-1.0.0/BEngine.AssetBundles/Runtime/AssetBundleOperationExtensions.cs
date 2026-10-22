namespace BEngine.AssetBundles;

public static class AssetBundleOperationExtensions
{
    public static AssetBundleRequest<byte[]> LoadBytesRequest(
        this IAssetBundleManager manager,
        string address)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new AssetBundleRequest<byte[]>(cancellationToken =>
            manager.LoadBytesAsync(address, cancellationToken));
    }

    public static AssetBundleRequest<string> LoadTextRequest(
        this IAssetBundleManager manager,
        string address)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new AssetBundleRequest<string>(cancellationToken =>
            manager.LoadTextAsync(address, cancellationToken));
    }
}
