using System.Text;
using YamlDotNet.Serialization;

namespace BEngine.AssetBundles;

/// <summary>A build and maintenance unit. Asset addresses remain global across packages.</summary>
public sealed class AssetBundlePackageDescriptor
{
    public const string DefaultName = "default";

    [YamlMember(Order = 0)] public string Name { get; set; } = string.Empty;
    /// <summary>A content fingerprint, independent of the containing release version.</summary>
    [YamlMember(Order = 1)] public string Version { get; set; } = string.Empty;
    [YamlMember(Order = 2)] public List<string> Bundles { get; set; } = [];

    /// <summary>Computes a stable package fingerprint without including release download routes.</summary>
    public static string ComputeVersion(AssetBundleCatalog catalog, IEnumerable<string> bundleNames)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(bundleNames);
        var names = bundleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selected = catalog.Bundles.Where(bundle => names.Contains(bundle.Name))
            .OrderBy(bundle => bundle.Name, StringComparer.Ordinal).ToArray();
        if (selected.Length != names.Count)
            throw new InvalidDataException("A package fingerprint references a missing bundle.");
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("BEngine.AssetBundlePackage.v1");
            writer.Write(selected.Length);
            foreach (var bundle in selected)
            {
                writer.Write(bundle.Name);
                writer.Write(bundle.Sha256.ToLowerInvariant());
                writer.Write(bundle.Size);
                var dependencies = bundle.Dependencies.OrderBy(value => value, StringComparer.Ordinal).ToArray();
                writer.Write(dependencies.Length);
                foreach (var dependency in dependencies) writer.Write(dependency);
            }
            var assets = catalog.Assets.Where(asset => names.Contains(asset.Bundle))
                .OrderBy(asset => asset.Address, StringComparer.Ordinal).ToArray();
            writer.Write(assets.Length);
            foreach (var asset in assets)
            {
                writer.Write(asset.Address);
                writer.Write(asset.Guid.ToByteArray());
                writer.Write((asset.OwnerGuid == Guid.Empty ? asset.Guid : asset.OwnerGuid).ToByteArray());
                writer.Write(asset.LocalIdentifier);
                writer.Write(asset.Bundle);
                writer.Write(asset.Entry);
                writer.Write(asset.AssetType);
                writer.Write(asset.Sha256.ToLowerInvariant());
                writer.Write(asset.Size);
                writer.Write(asset.Importer ?? string.Empty);
                writer.Write(asset.ImporterSettings.Count);
                foreach (var setting in asset.ImporterSettings.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    writer.Write(setting.Key);
                    writer.Write(setting.Value);
                }
            }
        }
        return AssetBundleCatalogSerializer.ComputeSha256(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }
}

internal static class AssetBundlePackageUtility
{
    internal static IReadOnlyList<AssetBundlePackageDescriptor> GetPackages(AssetBundleCatalog catalog) =>
        catalog.Packages.Count > 0 ? catalog.Packages :
        [new AssetBundlePackageDescriptor
        {
            Name = AssetBundlePackageDescriptor.DefaultName,
            Version = AssetBundlePackageDescriptor.ComputeVersion(catalog,
                catalog.Bundles.Select(static bundle => bundle.Name)),
            Bundles = catalog.Bundles.Select(static bundle => bundle.Name)
                .OrderBy(static name => name, StringComparer.Ordinal).ToList()
        }];
}
