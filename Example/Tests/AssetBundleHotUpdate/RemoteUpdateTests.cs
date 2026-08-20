using BEngine.AssetBundles;
using BEngine.Editor;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class RemoteUpdateTests
{
    internal static async Task RunAsync(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        await RetryActivateRollbackAndReuseContent(workspace, builds).ConfigureAwait(false);
        await FailedVerificationLeavesOldVersionActive(workspace, builds).ConfigureAwait(false);
    }

    private static async Task RetryActivateRollbackAndReuseContent(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        var handler = CreateRemote(builds.VersionTwo);
        var latestPath = RemotePath("latest.json");
        var changedBundle = builds.VersionTwo.Catalog.Bundles.Single(item => item.Name == "shared");
        var unchangedBundle = builds.VersionTwo.Catalog.Bundles.Single(item => item.Name == "main");
        var changedBundlePath = RemotePath(
            builds.VersionTwo.Version.Version, "bundles", changedBundle.FileName);
        var unchangedBundlePath = RemotePath(
            builds.VersionTwo.Version.Version, "bundles", unchangedBundle.FileName);
        handler.FailNext(latestPath);
        handler.FailNext(changedBundlePath);
        using var http = new HttpClient(handler, disposeHandler: true);
        var cache = Path.Combine(workspace.Root, "Caches", "remote-success");
        await using var manager = RuntimeAssetBundleTests.CreateManager(
            cache, builds.VersionOne.PackageDirectory, http);
        await manager.InitializeAsync().ConfigureAwait(false);

        var plan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        TestAssert.That(plan.HasUpdates && plan.TargetVersion.Version == "2.0.0" &&
                        plan.Downloads.Count == 1 && plan.Downloads[0].Name == "shared",
            "Remote planning did not reuse the unchanged content hash across versions.");
        TestAssert.That(handler.RequestCount(latestPath) == 2,
            "A transient version request was not retried exactly once.");
        var result = await manager.ApplyUpdateAsync(plan).ConfigureAwait(false);
        TestAssert.That(result.Updated && result.PreviousVersion == "1.0.0" &&
                        result.ActiveVersion == "2.0.0" && result.DownloadedBundleCount == 1,
            "The verified update did not report its atomic version transition.");
        TestAssert.That(handler.RequestCount(changedBundlePath) == 2,
            "A transient bundle download was not retried exactly once.");
        TestAssert.That(handler.RequestCount(unchangedBundlePath) == 0,
            "An unchanged content-addressed bundle was downloaded again.");
        TestAssert.That(!Directory.EnumerateFiles(manager.StagingDirectory, "*.part").Any(),
            "A successful update left a partial staging file behind.");

        var activePointer = AssetBundleCatalogSerializer.DeserializeVersion(
            await File.ReadAllBytesAsync(manager.ActivePointerPath).ConfigureAwait(false));
        var previousPointer = AssetBundleCatalogSerializer.DeserializeVersion(
            await File.ReadAllBytesAsync(manager.PreviousPointerPath).ConfigureAwait(false));
        TestAssert.That(activePointer.Version == "2.0.0" && previousPointer.Version == "1.0.0",
            "Active and rollback pointers were not published in the expected order.");
        await using (var updated = await manager.LoadTextAsync(
                         AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false))
            TestAssert.That(updated.Value == "shared-v2", "The active update served stale asset bytes.");
        _ = manager.UnloadUnused();

        var requestCountBeforeRollback = handler.RequestCount(changedBundlePath);
        TestAssert.That(await manager.RollbackAsync().ConfigureAwait(false) &&
                        manager.ActiveVersion?.Version == "1.0.0",
            "Rollback did not atomically restore the previous verified catalog.");
        await using (var rolledBack = await manager.LoadTextAsync(
                         AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false))
            TestAssert.That(rolledBack.Value == "shared-v1", "Rollback did not restore old asset bytes.");
        TestAssert.That(handler.RequestCount(changedBundlePath) == requestCountBeforeRollback,
            "Rollback downloaded content instead of switching verified pointers.");
        _ = manager.UnloadUnused();

        await using var restarted = RuntimeAssetBundleTests.CreateManager(
            cache, builds.VersionOne.PackageDirectory);
        await restarted.InitializeAsync().ConfigureAwait(false);
        TestAssert.That(restarted.ActiveVersion?.Version == "1.0.0",
            "The rolled-back active pointer did not survive an offline restart.");
        TestAssert.That(await restarted.RollbackAsync().ConfigureAwait(false) &&
                        restarted.ActiveVersion?.Version == "2.0.0",
            "The previous verified update was unavailable after an offline restart.");
        await using var cached = await restarted.LoadTextAsync(
            AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false);
        TestAssert.That(cached.Value == "shared-v2",
            "The offline cache did not retain the verified downloaded object.");
    }

    private static async Task FailedVerificationLeavesOldVersionActive(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        var handler = CreateRemote(builds.VersionTwo);
        var changedBundle = builds.VersionTwo.Catalog.Bundles.Single(item => item.Name == "shared");
        var bundlePath = RemotePath(builds.VersionTwo.Version.Version, "bundles", changedBundle.FileName);
        var corrupt = File.ReadAllBytes(Path.Combine(
            builds.VersionTwo.VersionDirectory, "bundles", changedBundle.FileName));
        corrupt[corrupt.Length / 2] ^= 0x5A;
        handler.Replace(bundlePath, corrupt);
        using var http = new HttpClient(handler, disposeHandler: true);
        var cache = Path.Combine(workspace.Root, "Caches", "remote-failure");
        await using var manager = RuntimeAssetBundleTests.CreateManager(
            cache, builds.VersionOne.PackageDirectory, http);
        await manager.InitializeAsync().ConfigureAwait(false);
        var catalogPath = RemotePath(builds.VersionTwo.Version.Version, "catalog.json");
        var validCatalog = File.ReadAllBytes(Path.Combine(builds.VersionTwo.VersionDirectory, "catalog.json"));
        var corruptCatalog = (byte[])validCatalog.Clone();
        corruptCatalog[corruptCatalog.Length / 2] ^= 0x01;
        handler.Replace(catalogPath, corruptCatalog);
        await TestAssert.ThrowsAsync<InvalidDataException>(
            () => manager.CheckForUpdatesAsync(), "SHA256 verification").ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == "1.0.0",
            "A corrupt remote catalog changed the active version during update planning.");
        handler.Replace(catalogPath, validCatalog);
        var plan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);

        await TestAssert.ThrowsAsync<InvalidDataException>(
            () => manager.ApplyUpdateAsync(plan), "content verification").ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == "1.0.0" &&
                        !File.Exists(manager.ActivePointerPath),
            "A failed download verification replaced the old active version.");
        TestAssert.That(!Directory.EnumerateFiles(manager.StagingDirectory, "*.part").Any() &&
                        !File.Exists(Path.Combine(manager.ObjectsDirectory, changedBundle.FileName)),
            "A failed download was promoted from staging into the object cache.");
        await using var oldAsset = await manager.LoadTextAsync(
            AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false);
        TestAssert.That(oldAsset.Value == "shared-v1",
            "The previous built-in version stopped loading after a failed update.");
    }

    private static ScriptedHttpMessageHandler CreateRemote(AssetBundleBuildResult build)
    {
        var handler = new ScriptedHttpMessageHandler();
        handler.Add(RemotePath("latest.json"),
            File.ReadAllBytes(Path.Combine(build.PackageDirectory, "latest.json")));
        handler.Add(RemotePath(build.Version.Version, "catalog.json"),
            File.ReadAllBytes(Path.Combine(build.VersionDirectory, "catalog.json")));
        foreach (var descriptor in build.Catalog.Bundles)
            handler.Add(RemotePath(build.Version.Version, "bundles", descriptor.FileName),
                File.ReadAllBytes(Path.Combine(build.VersionDirectory, "bundles", descriptor.FileName)));
        return handler;
    }

    private static string RemotePath(params string[] segments) =>
        "/content/" + string.Join('/', segments);
}
