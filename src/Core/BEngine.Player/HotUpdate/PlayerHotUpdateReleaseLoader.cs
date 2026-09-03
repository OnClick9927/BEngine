using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;

namespace BEngine.Player;

internal static class PlayerHotUpdateReleaseLoader
{
    internal static async BValueTask<ManagedCodeRelease?> LoadAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager? assetBundles,
        PlayerManagedCodeStage stage = PlayerManagedCodeStage.Legacy,
        PlayerBuiltInResourceProvider? builtInResources = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (stage == PlayerManagedCodeStage.Aot)
        {
            if (builtInResources is null ||
                !builtInResources.ContainsAddress(ManagedCodeReleaseManifest.DefaultAddress)) return null;
            return await LoadFromBuiltInResourcesAsync(builtInResources, cancellationToken)
                .ConfigureAwait(false);
        }
        if (assetBundles is { IsInitialized: true } &&
            assetBundles.EnumerateAddresses().Contains(
                ManagedCodeReleaseManifest.DefaultAddress, StringComparer.OrdinalIgnoreCase))
            return await LoadFromAssetBundlesAsync(assetBundles, cancellationToken).ConfigureAwait(false);
        if (stage == PlayerManagedCodeStage.HotUpdate) return null;
        return await LoadWorkspaceReleaseAsync(workspace, cancellationToken).ConfigureAwait(false);
    }

    private static async BValueTask<ManagedCodeRelease> LoadFromBuiltInResourcesAsync(
        PlayerBuiltInResourceProvider resources,
        CancellationToken cancellationToken)
    {
        var manifestBytes = await resources.ReadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress, cancellationToken).ConfigureAwait(false);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestBytes);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        foreach (var module in manifest.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assembly = await resources.ReadBytesAsync(module.AssemblyAddress, cancellationToken)
                .ConfigureAwait(false);
            byte[] symbols = [];
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) &&
                resources.ContainsAddress(module.SymbolsAddress))
                symbols = await resources.ReadBytesAsync(module.SymbolsAddress, cancellationToken)
                    .ConfigureAwait(false);
            modules.Add(new ManagedCodeModule(
                module.Name, module.BuildId, assembly, symbols, module.Dependencies));
        }
        return new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
    }

    private static async BValueTask<ManagedCodeRelease> LoadFromAssetBundlesAsync(
        IAssetBundleManager assetBundles,
        CancellationToken cancellationToken)
    {
        await using var manifestHandle = await assetBundles.LoadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress, cancellationToken).ConfigureAwait(false);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        foreach (var module in manifest.Modules)
        {
            await using var assembly = await assetBundles.LoadBytesAsync(
                module.AssemblyAddress, cancellationToken).ConfigureAwait(false);
            byte[]? symbols = null;
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) &&
                assetBundles.EnumerateAddresses().Contains(module.SymbolsAddress, StringComparer.OrdinalIgnoreCase))
            {
                await using var symbolsHandle = await assetBundles.LoadBytesAsync(
                    module.SymbolsAddress, cancellationToken).ConfigureAwait(false);
                symbols = (byte[])symbolsHandle.Value.Clone();
            }
            modules.Add(new ManagedCodeModule(
                module.Name, module.BuildId, assembly.Value, symbols ?? [], module.Dependencies));
        }
        return new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
    }

    private static async BValueTask<ManagedCodeRelease?> LoadWorkspaceReleaseAsync(
        ProjectWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var inputs = RuntimeManagedCodeReleaseInputCollector.Collect(workspace).Assemblies;
        if (inputs.Count == 0) return null;
        var modules = new List<ManagedCodeModule>(inputs.Count);
        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var image = await File.ReadAllBytesAsync(input.AssemblyPath, cancellationToken)
                .ConfigureAwait(false);
            var symbols = input.SymbolsPath is null
                ? null
                : await File.ReadAllBytesAsync(input.SymbolsPath, cancellationToken).ConfigureAwait(false);
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
