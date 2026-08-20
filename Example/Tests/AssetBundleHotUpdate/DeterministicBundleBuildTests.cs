using BEngine.AssetBundles;
using BEngine.Editor;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class DeterministicBundleBuildTests
{
    internal sealed record Builds(AssetBundleBuildResult VersionOne, AssetBundleBuildResult VersionTwo);

    internal static Builds Run(AssetBundleTestWorkspace workspace)
    {
        var first = workspace.Build("1.0.0", "shared-v1", "ordered");
        var reordered = workspace.Build("1.0.0", "shared-v1", "reordered", reverseInput: true);
        VerifyBuildsAreIdentical(first, reordered);

        var reused = workspace.Build("1.0.0", "shared-v1", "ordered", reverseInput: true);
        TestAssert.That(reused.ReusedExistingVersion,
            "Rebuilding an identical immutable version did not reuse the published version.");

        var changed = workspace.Build("2.0.0", "shared-v2", "changed", reverseInput: true);
        var firstShared = first.Catalog.Bundles.Single(item => item.Name == "shared");
        var changedShared = changed.Catalog.Bundles.Single(item => item.Name == "shared");
        var firstMain = first.Catalog.Bundles.Single(item => item.Name == "main");
        var changedMain = changed.Catalog.Bundles.Single(item => item.Name == "main");
        TestAssert.That(firstShared.Sha256 != changedShared.Sha256,
            "Changing an asset did not change its bundle content hash.");
        TestAssert.That(firstMain.Sha256 == changedMain.Sha256,
            "An unchanged bundle changed hash only because another bundle or version changed.");
        return new Builds(first, changed);
    }

    private static void VerifyBuildsAreIdentical(
        AssetBundleBuildResult first,
        AssetBundleBuildResult second)
    {
        foreach (var file in new[] { "catalog.json", "version.json" })
        {
            var firstBytes = File.ReadAllBytes(Path.Combine(first.VersionDirectory, file));
            var secondBytes = File.ReadAllBytes(Path.Combine(second.VersionDirectory, file));
            TestAssert.That(firstBytes.AsSpan().SequenceEqual(secondBytes),
                $"Deterministic build metadata differed for '{file}'.");
        }

        TestAssert.That(first.Catalog.Bundles.Select(item => item.FileName).SequenceEqual(
                second.Catalog.Bundles.Select(item => item.FileName), StringComparer.Ordinal),
            "Equivalent builds produced different bundle names.");
        foreach (var descriptor in first.Catalog.Bundles)
        {
            var firstBytes = File.ReadAllBytes(Path.Combine(
                first.VersionDirectory, "bundles", descriptor.FileName));
            var secondBytes = File.ReadAllBytes(Path.Combine(
                second.VersionDirectory, "bundles", descriptor.FileName));
            TestAssert.That(firstBytes.AsSpan().SequenceEqual(secondBytes),
                $"Bundle '{descriptor.Name}' changed with input order, timestamp, or output directory.");
            TestAssert.That(AssetBundleCatalogSerializer.ComputeSha256(firstBytes) == descriptor.Sha256,
                $"Bundle '{descriptor.Name}' file name does not match its content hash.");
        }
        TestAssert.That(!first.Catalog.Assets.Any(item =>
                item.Address.Contains("/Editor/", StringComparison.OrdinalIgnoreCase)),
            "Editor-only content was included in a runtime asset bundle.");
    }
}
