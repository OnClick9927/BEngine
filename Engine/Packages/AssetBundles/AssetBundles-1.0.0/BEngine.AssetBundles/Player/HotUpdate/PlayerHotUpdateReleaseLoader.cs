using BEngine.AssetBundles;
using BEngine.Player.HotUpdate;

namespace BEngine.Player;

internal static class PlayerHotUpdateReleaseLoader
{
    internal static async BValueTask<ManagedCodeRelease?> LoadAsync(
        IAssetBundleManager? assetBundles,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.Legacy,
        PlayerBuiltInResourceProvider? builtInResources = null,
        BCancellationToken cancellationToken = default)
    {
        if (stage == PlayerManagedCodeStage.Aot)
        {
            if (builtInResources is null ||
                !builtInResources.ContainsAddress(ManagedCodeReleaseManifest.DefaultAddress)) return null;
            return await LoadFromBuiltInResourcesAsync(builtInResources, cancellationToken);
        }
        if (assetBundles is { IsInitialized: true } &&
            assetBundles.EnumerateAddresses().Contains(
                ManagedCodeReleaseManifest.DefaultAddress, StringComparer.OrdinalIgnoreCase))
            return await LoadFromAssetBundlesAsync(assetBundles, cancellationToken);
        // The publishing/editor pipeline resolves packages and assembly paths.
        // A Player consumes only that complete release through AssetBundles;
        // it never scans a source workspace or falls back to loose DLL files.
        return null;
    }

    private static async BValueTask<ManagedCodeRelease> LoadFromBuiltInResourcesAsync(
        PlayerBuiltInResourceProvider resources,
        BCancellationToken cancellationToken)
    {
        using var operation = new ManagedCodeReleaseLoadOperation((address, context) =>
        {
            context.cancellationToken.ThrowIfCancellationRequested();
            return BValueTask<ManagedCodeResource>.FromResult(new(resources.ReadBytes(address)));
        }, resourceExists: resources.ContainsAddress, cancellationToken: cancellationToken);
        while (!operation.isDone)
        {
            BEngine.Hosting.HostStartupEnvironment.Current?.Report(.35f + .3f * operation.progress, "managedCode");
            await BTask.Yield();
        }
        await operation;
        BEngine.Hosting.HostStartupEnvironment.Current?.Report(.65f, "managedCode");
        return operation.release!;
    }

    private static async BValueTask<ManagedCodeRelease> LoadFromAssetBundlesAsync(
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken)
    {
        var addresses = assetBundles.EnumerateAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var operation = new ManagedCodeReleaseLoadOperation(async (address, context) =>
        {
            var handle = await assetBundles.LoadBytesAsync(address, context.cancellationToken);
            return new ManagedCodeResource(handle.Value, handle);
        }, resourceExists: addresses.Contains, cancellationToken: cancellationToken);
        await operation;
        return operation.release!;
    }

}
