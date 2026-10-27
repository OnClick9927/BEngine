namespace BEngine.AssetBundles;

public static class AssetBundleOperationExtensions
{
    public static AssetBundleInitializeOperation InitializeRequest(this IAssetBundleManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return new(manager);
    }
    public static AssetBundleRequest<byte[]> LoadBytesRequest(
        this IAssetBundleManager manager,
        string address)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new AssetBundleRequest<byte[]>((cancellationToken, tracker) => manager is AssetBundleManager runtime
            ? runtime.LoadBytesTrackedAsync(address, cancellationToken, tracker) : manager.LoadBytesAsync(address, cancellationToken));
    }

    public static AssetBundleRequest<string> LoadTextRequest(
        this IAssetBundleManager manager,
        string address)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        return new AssetBundleRequest<string>((cancellationToken, tracker) => manager is AssetBundleManager runtime
            ? runtime.LoadTextTrackedAsync(address, cancellationToken, tracker) : manager.LoadTextAsync(address, cancellationToken));
    }
}
