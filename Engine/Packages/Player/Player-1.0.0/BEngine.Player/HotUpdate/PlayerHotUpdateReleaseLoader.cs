using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Player;

internal static class PlayerHotUpdateReleaseLoader
{
    internal static async BValueTask<ManagedCodeRelease?> LoadAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager? assetBundles,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.Legacy,
        PlayerBuiltInResourceProvider? builtInResources = null,
        BCancellationToken cancellationToken = default,
        FileUtility? fileSystem = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        fileSystem ??= RuntimeServiceAccess.FileSystem;
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
        if (stage == PlayerManagedCodeStage.HotUpdate) return null;
        return LoadWorkspaceRelease(workspace, fileSystem, cancellationToken);
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
        await operation;
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

    private static ManagedCodeRelease? LoadWorkspaceRelease(
        ProjectWorkspace workspace,
        FileUtility fileSystem,
        BCancellationToken cancellationToken)
    {
        var inputs = RuntimeManagedCodeReleaseInputCollector.Collect(workspace).Assemblies;
        if (inputs.Count == 0) return null;
        var modules = new List<ManagedCodeModule>(inputs.Count);
        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = fileSystem.ReadAllBytes(input.AssemblyPath);
            var symbols = input.SymbolsPath is null
                ? null
                : fileSystem.ReadAllBytes(input.SymbolsPath);
            releaseHash.AppendData(Encoding.UTF8.GetBytes($"{input.Name}\0{input.BuildId}\0"));
            releaseHash.AppendData(SHA256.HashData(image));
            modules.Add(new ManagedCodeModule(
                input.Name,
                input.BuildId,
                image,
                symbols ?? [],
                input.Dependencies));
        }
        var releaseId = Convert.ToHexString(releaseHash.GetHashAndReset()).ToLowerInvariant()[..24];
        return new ManagedCodeRelease($"workspace-{releaseId}", modules);
    }
}
