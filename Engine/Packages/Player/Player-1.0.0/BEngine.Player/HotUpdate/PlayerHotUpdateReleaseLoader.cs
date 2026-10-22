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
            return LoadFromBuiltInResources(builtInResources, cancellationToken);
        }
        if (assetBundles is { IsInitialized: true } &&
            assetBundles.EnumerateAddresses().Contains(
                ManagedCodeReleaseManifest.DefaultAddress, StringComparer.OrdinalIgnoreCase))
            return await LoadFromAssetBundlesAsync(assetBundles, cancellationToken);
        if (stage == PlayerManagedCodeStage.HotUpdate) return null;
        return LoadWorkspaceRelease(workspace, fileSystem, cancellationToken);
    }

    private static ManagedCodeRelease LoadFromBuiltInResources(
        PlayerBuiltInResourceProvider resources,
        BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var manifestBytes = resources.ReadBytes(ManagedCodeReleaseManifest.DefaultAddress);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestBytes);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        foreach (var module in manifest.Modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assembly = resources.ReadBytes(module.AssemblyAddress);
            byte[] symbols = [];
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) &&
                resources.ContainsAddress(module.SymbolsAddress))
                symbols = resources.ReadBytes(module.SymbolsAddress);
            modules.Add(new ManagedCodeModule(
                module.Name, module.BuildId, assembly, symbols, module.Dependencies));
        }
        return new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
    }

    private static async BValueTask<ManagedCodeRelease> LoadFromAssetBundlesAsync(
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken)
    {
        using var manifestHandle = await assetBundles.LoadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress, cancellationToken);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        foreach (var module in manifest.Modules)
        {
            using var assembly = await assetBundles.LoadBytesAsync(
                module.AssemblyAddress, cancellationToken);
            byte[]? symbols = null;
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) &&
                assetBundles.EnumerateAddresses().Contains(module.SymbolsAddress, StringComparer.OrdinalIgnoreCase))
            {
                using var symbolsHandle = await assetBundles.LoadBytesAsync(
                    module.SymbolsAddress, cancellationToken);
                symbols = (byte[])symbolsHandle.Value.Clone();
            }
            modules.Add(new ManagedCodeModule(
                module.Name, module.BuildId, assembly.Value, symbols ?? [], module.Dependencies));
        }
        return new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
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
