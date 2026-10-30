using System.Buffers;
using System.Security.Cryptography;
using System.Text;
namespace BEngine.AssetBundles;
internal static class OwnedPackageFingerprint
{
    internal sealed class Input(bool hashesLowercase = false)
    {
        internal bool HashesLowercase { get; } = hashesLowercase;
        private readonly List<BundleInput> _bundles = [];
        private readonly List<AssetInput> _assets = [];
        private bool _bundlesOrdered = true, _assetsOrdered = true, _consumed;
        internal void AddBundle(AssetBundleDescriptor bundle)
        {
            if (_bundles.Count > 0 && StringComparer.Ordinal.Compare(_bundles[^1].Bundle.Name, bundle.Name) > 0) _bundlesOrdered = false;
            var ordered = true;
            for (var index = 1; index < bundle.Dependencies.Count; index++)
                if (StringComparer.Ordinal.Compare(bundle.Dependencies[index - 1], bundle.Dependencies[index]) > 0) ordered = false;
            _bundles.Add(new(bundle, ordered));
        }
        internal void AddAsset(AssetBundleAsset asset, bool settingsOrdered)
        {
            if (_assets.Count > 0 && StringComparer.Ordinal.Compare(_assets[^1].Asset.Address, asset.Address) > 0) _assetsOrdered = false;
            _assets.Add(new(asset, settingsOrdered));
        }
        internal IReadOnlyList<BundleInput> OrderedBundles() => _bundlesOrdered ? _bundles : _bundles.OrderBy(value => value.Bundle.Name, StringComparer.Ordinal).ToArray();
        internal IReadOnlyList<AssetInput> OrderedAssets() => _assetsOrdered ? _assets : _assets.OrderBy(value => value.Asset.Address, StringComparer.Ordinal).ToArray();
        internal void Claim() { if (_consumed) throw new InvalidOperationException("Owned fingerprint input already consumed."); _consumed = true; }
    }
    internal readonly record struct BundleInput(AssetBundleDescriptor Bundle, bool DependenciesOrdered);
    internal readonly record struct AssetInput(AssetBundleAsset Asset, bool SettingsOrdered);
    internal static string ComputeVersion(Input input) => AssetBundlePackageDescriptor.ComputeOwnedVersion(input);
}
