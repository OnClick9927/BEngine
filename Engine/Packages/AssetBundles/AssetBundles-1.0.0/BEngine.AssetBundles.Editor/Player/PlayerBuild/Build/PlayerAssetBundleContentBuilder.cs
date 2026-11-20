using System.Security.Cryptography;
using System.Text;
using BEngine.AssetBundles;
using BEngine.Player.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor.Build;

/// <summary>Adapts application release inputs to the neutral AssetBundle content contract.</summary>
internal static class PlayerAssetBundleContentBuilder
{
    internal static IReadOnlyList<AssetBundleBuildContentBundle> Create(
        RuntimeManagedCodeReleaseInputSet inputs,
        bool includeManagedSymbols,
        FileUtility fileSystem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(fileSystem);
        cancellationToken.ThrowIfCancellationRequested();
        bool __queryResult783;
        {
            var __querySource783 = inputs.Assemblies;
            ArgumentNullException.ThrowIfNull(__querySource783, "source");
            __queryResult783 = false;
            var __querySource783Type = __querySource783.GetType();
            if (__querySource783Type == typeof(BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput[]) || __querySource783Type == typeof(System.Collections.Generic.List<BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput>))
            {
                System.ReadOnlySpan<BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput> __querySource783Span = __querySource783Type == typeof(BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput[]) ? (BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput[])(object)__querySource783 : System.Runtime.InteropServices.CollectionsMarshal.AsSpan((System.Collections.Generic.List<BEngine.Editor.Build.ProjectSystem.RuntimeManagedCodeReleaseInput>)(object)__querySource783);
                foreach (var __queryItem783 in __querySource783Span)
                {
                    if ((__queryItem783.Name.Equals("AOT", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult783 = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var __queryItem783 in __querySource783)
                {
                    if ((__queryItem783.Name.Equals("AOT", StringComparison.OrdinalIgnoreCase)))
                    {
                        __queryResult783 = true;
                        break;
                    }
                }
            }
        }
        
        if (__queryResult783)
            throw new InvalidDataException("The HotUpdate AssetBundle release cannot contain the project's built-in AOT assembly.");
        var bundles = new List<AssetBundleBuildContentBundle>(2);
        if (inputs.PackageResources.Count != 0)
        {
            var assets = new List<AssetBundleBuildContent>(inputs.PackageResources.Count);
            foreach (var input in inputs.PackageResources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                assets.Add(new AssetBundleBuildContent
                {
                    Address = input.Address,
                    FilePath = input.FilePath,
                    AssetType = "PackageResource",
                    Importer = "BEnginePackageResource",
                    ImporterSettings = new Dictionary<string, string>(),
                    ExpectedSha256 = input.Sha256,
                    ExpectedSize = input.Size
                });
            }
            bundles.Add(new AssetBundleBuildContentBundle
            {
                Name = "bengine-packages", PackageName = "bengine", Assets = assets
            });
        }
        var codeAssets = CaptureManagedCode(inputs.Assemblies, includeManagedSymbols, fileSystem, cancellationToken);
        if (codeAssets.Count != 0)
            bundles.Add(new AssetBundleBuildContentBundle
            {
                Name = "bengine-hotupdate", PackageName = "bengine", Assets = codeAssets
            });
        return bundles;
    }

    private static IReadOnlyList<AssetBundleBuildContent> CaptureManagedCode(
        IReadOnlyList<RuntimeManagedCodeReleaseInput> inputs,
        bool includeManagedSymbols,
        FileUtility fileSystem,
        CancellationToken cancellationToken)
    {
        if (inputs.Count == 0) return [];
        using var releaseHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var manifest = new ManagedCodeReleaseManifest();
        var assets = new List<AssetBundleBuildContent>(inputs.Count * 2 + 1);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var assembly = fileSystem.ReadAllBytes(input.AssemblyPath);
            var assemblyHash = AssetBundleCatalogSerializer.ComputeSha256(assembly);
            if (assembly.LongLength != input.AssemblySize ||
                !assemblyHash.Equals(input.AssemblySha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Runtime assembly '{input.Name}' changed after the release input snapshot was captured.");
            var symbols = includeManagedSymbols && input.SymbolsPath is not null
                ? fileSystem.ReadAllBytes(input.SymbolsPath)
                : null;
            var assemblyAddress = $"Assets/__BEngine/HotUpdate/{input.Name}.dll";
            var symbolsAddress = symbols is { Length: > 0 }
                ? $"Assets/__BEngine/HotUpdate/{input.Name}.pdb"
                : null;
            releaseHash.AppendData(Encoding.UTF8.GetBytes($"{input.Name}\0{input.BuildId}\0"));
            releaseHash.AppendData(SHA256.HashData(assembly));
            assets.Add(Memory(assemblyAddress, assembly, "ManagedAssembly"));
            if (symbolsAddress is not null) assets.Add(Memory(symbolsAddress, symbols!, "ManagedSymbols"));
            manifest.Modules.Add(new ManagedCodeModuleManifest
            {
                Name = input.Name,
                BuildId = input.BuildId,
                AssemblyAddress = assemblyAddress,
                SymbolsAddress = symbolsAddress,
                Dependencies = input.Dependencies.ToList()
            });
        }
        manifest.ReleaseId = "code-" +
            Convert.ToHexStringLower(releaseHash.GetHashAndReset())[..24];
        assets.Add(Memory(ManagedCodeReleaseManifest.DefaultAddress,
            ManagedCodeReleaseManifestSerializer.SerializeRuntime(manifest, cancellationToken),
            "ManagedCodeReleaseManifest"));
        return assets;
    }

    private static AssetBundleBuildContent Memory(string address, byte[] content, string assetType) => new()
    {
        Address = address,
        Content = content,
        AssetType = assetType,
        Importer = "BEngineHotUpdate",
        ImporterSettings = new Dictionary<string, string>(),
        ExpectedSha256 = AssetBundleCatalogSerializer.ComputeSha256(content),
        ExpectedSize = content.LongLength
    };
}
