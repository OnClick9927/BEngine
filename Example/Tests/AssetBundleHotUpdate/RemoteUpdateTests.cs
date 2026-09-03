using BEngine.AssetBundles;
using BEngine.Editor;
using BEngine.Networking;
using System.Collections.Concurrent;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class RemoteUpdateTests
{
    internal static async Task RunAsync(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        await FirstRemoteActivationCanRollbackToEmpty(workspace, builds.VersionTwo)
            .ConfigureAwait(false);
        await RetryActivateRollbackAndReuseContent(workspace, builds).ConfigureAwait(false);
        await RemotePointerCanReactivateCachedOlderVersion(workspace, builds).ConfigureAwait(false);
        await MinimalPointerRejectsMissingOrMismatchedVersionMetadata(workspace, builds)
            .ConfigureAwait(false);
        await MissingLatestDoesNotFallBackToLegacyVersionPointer(workspace, builds)
            .ConfigureAwait(false);
        await FailedVerificationLeavesOldVersionActive(workspace, builds).ConfigureAwait(false);
    }

    private static async Task FirstRemoteActivationCanRollbackToEmpty(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult remoteBuild)
    {
        var handler = CreateRemote(remoteBuild);
        using var http = new HttpClient(handler, disposeHandler: true);
        var cache = Path.Combine(workspace.Root, "Caches", "remote-first-activation");
        await using (var manager = RuntimeAssetBundleTests.CreateManager(
                         cache, builtInDirectory: null, http))
        {
            await manager.InitializeAsync().ConfigureAwait(false);
            TestAssert.That(manager.ActiveVersion is null &&
                            Directory.Exists(manager.ObjectsDirectory) &&
                            Directory.Exists(manager.CatalogsDirectory) &&
                            !Directory.Exists(manager.StagingDirectory),
                "An empty remote-only cache did not initialize without a built-in release.");

            var plan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            TestAssert.That(plan.HasUpdates &&
                            plan.Downloads.Count == remoteBuild.Catalog.Bundles.Count,
                "The first remote-only update did not require every remote bundle.");
            var result = await manager.ApplyUpdateAsync(plan).ConfigureAwait(false);
            TestAssert.That(result.Updated && string.IsNullOrEmpty(result.PreviousVersion) &&
                            result.ActiveVersion == remoteBuild.Version.Version &&
                            result.DownloadedBundleCount == remoteBuild.Catalog.Bundles.Count &&
                            manager.HasPendingActivation &&
                            File.Exists(manager.ActivePointerPath) &&
                            File.Exists(manager.PendingPointerPath) &&
                            !File.Exists(manager.PreviousPointerPath) &&
                            !Directory.Exists(manager.StagingDirectory),
                "The first remote-only release was not staged without a built-in rollback pointer.");
            TestAssert.That(remoteBuild.Catalog.Bundles.All(descriptor =>
                    File.Exists(Path.Combine(manager.ObjectsDirectory, descriptor.FileName))),
                "The first remote-only update did not persist all verified bundle objects.");
        }

        await using (var restarted = RuntimeAssetBundleTests.CreateManager(
                         cache, builtInDirectory: null))
        {
            await restarted.InitializeAsync().ConfigureAwait(false);
            TestAssert.That(restarted.ActiveVersion?.Version == remoteBuild.Version.Version &&
                            restarted.HasPendingActivation,
                "A pending first remote activation was not recovered after restart.");
            TestAssert.That(await restarted.RollbackAsync().ConfigureAwait(false) &&
                            restarted.ActiveVersion is null &&
                            !File.Exists(restarted.ActivePointerPath) &&
                            !File.Exists(restarted.PreviousPointerPath) &&
                            !File.Exists(restarted.PendingPointerPath),
                "A failed first remote activation did not roll back to an empty state.");
            await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await using var ignored = await restarted.LoadTextAsync(
                    AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false);
            }, "No asset bundle catalog is active").ConfigureAwait(false);

            await File.WriteAllBytesAsync(
                    restarted.PendingPointerPath,
                    AssetBundleCatalogSerializer.SerializeVersion(remoteBuild.Version))
                .ConfigureAwait(false);
        }

        await using var offline = RuntimeAssetBundleTests.CreateManager(
            cache, builtInDirectory: null);
        await offline.InitializeAsync().ConfigureAwait(false);
        TestAssert.That(offline.ActiveVersion is null &&
                        !File.Exists(offline.PendingPointerPath),
            "Offline restart did not remain empty or remove an orphan pending pointer.");
        var deleted = await offline.CleanupAsync().ConfigureAwait(false);
        TestAssert.That(deleted == remoteBuild.Catalog.Bundles.Count + 1 &&
                        !Directory.EnumerateFiles(offline.ObjectsDirectory).Any() &&
                        !Directory.EnumerateFiles(offline.CatalogsDirectory).Any(),
            "Empty-state cleanup did not remove the unreferenced first remote release.");
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
        var transfers = new ConcurrentQueue<AssetBundleHttpTransferDiagnostic>();
        var cache = Path.Combine(workspace.Root, "Caches", "remote-success");
        await using var manager = RuntimeAssetBundleTests.CreateManager(
            cache,
            builds.VersionOne.PackageDirectory,
            http,
            transfers.Enqueue,
            new Uri("https://diagnostic-user:diagnostic-password@asset-bundle.test/content/" +
                    "?access_token=must-not-be-logged#private"));
        await manager.InitializeAsync().ConfigureAwait(false);
        var provider = new AssetBundleResourceProvider(manager);
        TestAssert.That(provider.TryLoadAsset(
                            AssetBundleTestWorkspace.SpriteAddress,
                            "Resources",
                            typeof(Texture),
                            out var versionOneTexture),
            "The initial bundle version did not construct its Texture asset.");

        var plan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        TestAssert.That(plan.HasUpdates && plan.TargetVersion.Version == "2.0.0" &&
                        plan.Downloads.Count == 1 && plan.Downloads[0].Name == "shared",
            "Remote planning did not reuse the unchanged content hash across versions.");
        TestAssert.That(handler.RequestCount(latestPath) == 2,
            "A transient version request was not retried exactly once.");
        TestAssert.That(handler.RequestCount(RemotePath(
                            builds.VersionTwo.Version.Version, "version.json")) == 0,
            "A legacy complete latest manifest unexpectedly required separate version metadata.");
        plan.TargetCatalog.Version = "caller-mutated";
        plan.Downloads[0].Name = "caller-mutated";
        var concurrentResults = await Task.WhenAll(
            manager.ApplyUpdateAsync(plan),
            manager.ApplyUpdateAsync(plan)).ConfigureAwait(false);
        var result = concurrentResults.Single(item => item.Updated);
        TestAssert.That(concurrentResults.Count(item => item.Updated) == 1 &&
                        result.PreviousVersion == "1.0.0" &&
                        result.ActiveVersion == "2.0.0" && result.DownloadedBundleCount == 1,
            "Concurrent update submission did not produce one atomic version transition.");
        TestAssert.That(handler.RequestCount(changedBundlePath) == 2,
            "A transient bundle download was not retried exactly once.");
        TestAssert.That(handler.RequestCount(unchangedBundlePath) == 0,
            "An unchanged content-addressed bundle was downloaded again.");
        AssertHttpTransferDiagnostics(builds.VersionTwo, changedBundle, transfers);
        TestAssert.That(!Directory.Exists(manager.StagingDirectory),
            "A successful update left its staging directory behind.");
        TestAssert.That(provider.TryLoadAsset(
                            AssetBundleTestWorkspace.SpriteAddress,
                            "Resources",
                            typeof(Texture),
                            out var versionTwoTexture) &&
                        !ReferenceEquals(versionOneTexture, versionTwoTexture) &&
                        versionOneTexture.Id == versionTwoTexture.Id,
            "The resource provider reused a stale constructed asset after activating a new catalog.");

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
        TestAssert.That(provider.TryLoadAsset(
                            AssetBundleTestWorkspace.SpriteAddress,
                            "Resources",
                            typeof(Texture),
                            out var rolledBackTexture) &&
                        !ReferenceEquals(versionTwoTexture, rolledBackTexture),
            "The resource provider retained a constructed asset from the rolled-back catalog.");
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
        TestAssert.That(!Directory.Exists(manager.StagingDirectory) &&
                        !File.Exists(Path.Combine(manager.ObjectsDirectory, changedBundle.FileName)),
            "A failed download was promoted from staging into the object cache.");
        await using var oldAsset = await manager.LoadTextAsync(
            AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false);
        TestAssert.That(oldAsset.Value == "shared-v1",
            "The previous built-in version stopped loading after a failed update.");
    }

    private static async Task RemotePointerCanReactivateCachedOlderVersion(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        var handler = new ScriptedHttpMessageHandler();
        AddRemoteRelease(handler, builds.VersionOne);
        AddRemoteRelease(handler, builds.VersionTwo);
        var latestPath = RemotePath("latest.json");
        SetRemoteLatestPointer(handler, builds.VersionOne);
        using var http = new HttpClient(handler, disposeHandler: true);
        var cache = Path.Combine(workspace.Root, "Caches", "remote-authoritative-version");

        await using (var manager = RuntimeAssetBundleTests.CreateManager(
                         cache, builtInDirectory: null, http))
        {
            await manager.InitializeAsync().ConfigureAwait(false);

            var versionOnePlan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            var versionOneResult = await manager.ApplyUpdateAsync(versionOnePlan).ConfigureAwait(false);
            await manager.CommitPendingActivationAsync().ConfigureAwait(false);
            TestAssert.That(versionOneResult.Updated &&
                            versionOneResult.ActiveVersion == builds.VersionOne.Version.Version,
                "The remote-authoritative fixture did not first activate version one.");

            SetRemoteLatestPointer(handler, builds.VersionTwo);
            var versionTwoPlan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            var versionTwoResult = await manager.ApplyUpdateAsync(versionTwoPlan).ConfigureAwait(false);
            await manager.CommitPendingActivationAsync().ConfigureAwait(false);
            TestAssert.That(versionTwoResult.Updated &&
                            versionTwoResult.PreviousVersion == builds.VersionOne.Version.Version &&
                            versionTwoResult.ActiveVersion == builds.VersionTwo.Version.Version,
                "Advancing the remote pointer did not activate version two before the reverse switch.");

            var versionOneBundleRequests = builds.VersionOne.Catalog.Bundles.ToDictionary(
                descriptor => descriptor.Name,
                descriptor => handler.RequestCount(RemotePath(
                    builds.VersionOne.Version.Version, "bundles", descriptor.FileName)),
                StringComparer.OrdinalIgnoreCase);
            SetRemoteLatestPointer(handler, builds.VersionOne);
            var reversePlan = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            TestAssert.That(reversePlan.HasUpdates &&
                            reversePlan.TargetVersion.Version == builds.VersionOne.Version.Version &&
                            reversePlan.Downloads.Count == 0,
                "A remote latest pointer to cached version one was not planned as a zero-download transition.");

            var reverseResult = await manager.ApplyUpdateAsync(reversePlan).ConfigureAwait(false);
            TestAssert.That(reverseResult.Updated &&
                            reverseResult.PreviousVersion == builds.VersionTwo.Version.Version &&
                            reverseResult.ActiveVersion == builds.VersionOne.Version.Version &&
                            reverseResult.DownloadedBundleCount == 0 &&
                            reverseResult.DownloadedBytes == 0 &&
                            manager.HasPendingActivation,
                "The remote latest pointer did not atomically reactivate cached version one.");
            TestAssert.That(builds.VersionOne.Catalog.Bundles.All(descriptor =>
                    handler.RequestCount(RemotePath(
                        builds.VersionOne.Version.Version, "bundles", descriptor.FileName)) ==
                    versionOneBundleRequests[descriptor.Name]),
                "Reactivating cached version one unexpectedly downloaded one of its bundles again.");

            var active = AssetBundleCatalogSerializer.DeserializeVersion(
                await File.ReadAllBytesAsync(manager.ActivePointerPath).ConfigureAwait(false));
            var previous = AssetBundleCatalogSerializer.DeserializeVersion(
                await File.ReadAllBytesAsync(manager.PreviousPointerPath).ConfigureAwait(false));
            TestAssert.That(active.Version == builds.VersionOne.Version.Version &&
                            previous.Version == builds.VersionTwo.Version.Version,
                "The reverse transition did not persist active=v1 and previous=v2 pointers.");
            await using (var versionOne = await manager.LoadTextAsync(
                             AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false))
                TestAssert.That(versionOne.Value == "shared-v1",
                    "The reverse transition did not serve version-one content.");
            await manager.CommitPendingActivationAsync().ConfigureAwait(false);
        }

        AssertVersionPointerNoCacheHeaders(handler.RequestHeaders(latestPath));
        TestAssert.That(
            handler.RequestCount(RemotePath(
                builds.VersionOne.Version.Version, "version.json")) == 2 &&
            handler.RequestCount(RemotePath(
                builds.VersionTwo.Version.Version, "version.json")) == 1,
            "Each version-only latest pointer was not resolved through its immutable version metadata.");

        await using var restarted = RuntimeAssetBundleTests.CreateManager(
            cache, builtInDirectory: null);
        await restarted.InitializeAsync().ConfigureAwait(false);
        var restartedPrevious = AssetBundleCatalogSerializer.DeserializeVersion(
            await File.ReadAllBytesAsync(restarted.PreviousPointerPath).ConfigureAwait(false));
        TestAssert.That(restarted.ActiveVersion?.Version == builds.VersionOne.Version.Version &&
                        restartedPrevious.Version == builds.VersionTwo.Version.Version &&
                        !restarted.HasPendingActivation,
            "Restart did not preserve the remote-directed v2-to-v1 transition and its v2 rollback pointer.");
        await using var restartedVersionOne = await restarted.LoadTextAsync(
            AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false);
        TestAssert.That(restartedVersionOne.Value == "shared-v1",
            "Restart did not serve the remotely selected version-one content from cache.");
    }

    private static async Task MinimalPointerRejectsMissingOrMismatchedVersionMetadata(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        var handler = new ScriptedHttpMessageHandler();
        AddRemoteRelease(handler, builds.VersionOne);
        AddRemoteRelease(handler, builds.VersionTwo);
        SetRemoteLatestPointer(handler, builds.VersionTwo);
        using var http = new HttpClient(handler, disposeHandler: true);
        var transfers = new ConcurrentQueue<AssetBundleHttpTransferDiagnostic>();
        var cache = Path.Combine(workspace.Root, "Caches", "minimal-pointer-validation");
        await using var manager = RuntimeAssetBundleTests.CreateManager(
            cache, builds.VersionOne.PackageDirectory, http, transfers.Enqueue);
        await manager.InitializeAsync().ConfigureAwait(false);

        var versionTwoPath = RemotePath(builds.VersionTwo.Version.Version, "version.json");
        handler.Replace(versionTwoPath,
            AssetBundleCatalogSerializer.SerializeVersion(builds.VersionOne.Version));
        await TestAssert.ThrowsAsync<InvalidDataException>(
            () => manager.CheckForUpdatesAsync(), "versions do not match").ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == builds.VersionOne.Version.Version &&
                        handler.RequestCount(RemotePath(
                            builds.VersionTwo.Version.Version, "catalog.json")) == 0,
            "Mismatched version metadata reached the catalog or changed the active release.");

        var mismatchedPackage = AssetBundleCatalogSerializer.DeserializeVersion(
            AssetBundleCatalogSerializer.SerializeVersion(builds.VersionTwo.Version));
        mismatchedPackage.PackageName = "different-package";
        handler.Replace(versionTwoPath,
            AssetBundleCatalogSerializer.SerializeVersion(mismatchedPackage));
        await TestAssert.ThrowsAsync<InvalidDataException>(
            () => manager.CheckForUpdatesAsync(), "does not match").ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == builds.VersionOne.Version.Version &&
                        handler.RequestCount(RemotePath(
                            builds.VersionTwo.Version.Version, "catalog.json")) == 0,
            "Mismatched version-metadata package identity reached the catalog or changed the active release.");

        var missingVersion = "missing-v3";
        handler.Replace(RemotePath("latest.json"),
            AssetBundleCatalogSerializer.SerializeLatestPointer(new AssetBundleLatestPointer
            {
                PackageName = builds.VersionOne.Version.PackageName,
                Version = missingVersion
            }));
        await TestAssert.ThrowsAsync<HttpRequestException>(
            () => manager.CheckForUpdatesAsync(), "404").ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == builds.VersionOne.Version.Version &&
                        handler.RequestCount(RemotePath(missingVersion, "version.json")) == 1,
            "A missing pointed version manifest changed the active release or used a fallback.");

        var metadataTransfers = transfers.Where(item =>
            item.ResourceKind == AssetBundleHttpResourceKind.VersionMetadata).ToArray();
        TestAssert.That(metadataTransfers.Length == 3 &&
                        metadataTransfers.All(item => item.Endpoint.EndsWith(
                            "/version.json", StringComparison.Ordinal)),
            "Pointed version metadata did not have distinct HTTP diagnostics.");
    }

    private static async Task MissingLatestDoesNotFallBackToLegacyVersionPointer(
        AssetBundleTestWorkspace workspace,
        DeterministicBundleBuildTests.Builds builds)
    {
        var handler = new ScriptedHttpMessageHandler();
        AddRemoteRelease(handler, builds.VersionTwo);
        var legacyVersionPath = RemotePath("version.json");
        handler.Add(legacyVersionPath,
            File.ReadAllBytes(Path.Combine(builds.VersionTwo.VersionDirectory, "version.json")));
        using var http = new HttpClient(handler, disposeHandler: true);
        var cache = Path.Combine(workspace.Root, "Caches", "remote-missing-latest");
        await using var manager = RuntimeAssetBundleTests.CreateManager(
            cache, builds.VersionOne.PackageDirectory, http);
        await manager.InitializeAsync().ConfigureAwait(false);

        await TestAssert.ThrowsAsync<HttpRequestException>(
            () => manager.CheckForUpdatesAsync(), "404").ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == builds.VersionOne.Version.Version &&
                        handler.RequestCount(RemotePath("latest.json")) == 1 &&
                        handler.RequestCount(legacyVersionPath) == 0,
            "A missing latest.json fell back to the legacy root version.json target.");
        AssertVersionPointerNoCacheHeaders(
            handler.RequestHeaders(RemotePath("latest.json")), minimumRequestCount: 1);
    }

    private static ScriptedHttpMessageHandler CreateRemote(AssetBundleBuildResult build)
    {
        var handler = new ScriptedHttpMessageHandler();
        SetRemoteLatest(handler, build);
        AddRemoteRelease(handler, build);
        return handler;
    }

    private static void AddRemoteRelease(
        ScriptedHttpMessageHandler handler,
        AssetBundleBuildResult build)
    {
        handler.Add(RemotePath(build.Version.Version, "version.json"),
            File.ReadAllBytes(Path.Combine(build.VersionDirectory, "version.json")));
        handler.Add(RemotePath(build.Version.Version, "catalog.json"),
            File.ReadAllBytes(Path.Combine(build.VersionDirectory, "catalog.json")));
        foreach (var descriptor in build.Catalog.Bundles)
            handler.Add(RemotePath(build.Version.Version, "bundles", descriptor.FileName),
                File.ReadAllBytes(Path.Combine(build.VersionDirectory, "bundles", descriptor.FileName)));
    }

    private static void SetRemoteLatest(
        ScriptedHttpMessageHandler handler,
        AssetBundleBuildResult build) =>
        handler.Replace(RemotePath("latest.json"),
            File.ReadAllBytes(Path.Combine(build.VersionDirectory, "version.json")));

    private static void SetRemoteLatestPointer(
        ScriptedHttpMessageHandler handler,
        AssetBundleBuildResult build) =>
        handler.Replace(RemotePath("latest.json"),
            AssetBundleCatalogSerializer.SerializeLatestPointer(new AssetBundleLatestPointer
            {
                PackageName = build.Version.PackageName,
                Version = build.Version.Version
            }));

    private static void AssertVersionPointerNoCacheHeaders(
        IReadOnlyList<IReadOnlyDictionary<string, string[]>> requests,
        int minimumRequestCount = 3)
    {
        TestAssert.That(requests.Count >= minimumRequestCount,
            "The reverse-version scenario did not request the remote latest pointer for every transition.");
        TestAssert.That(requests.All(headers =>
            {
                var cacheControl = headers.TryGetValue("Cache-Control", out var cacheValues)
                    ? string.Join(",", cacheValues)
                    : string.Empty;
                var pragma = headers.TryGetValue("Pragma", out var pragmaValues)
                    ? string.Join(",", pragmaValues)
                    : string.Empty;
                return cacheControl.Contains("no-cache", StringComparison.OrdinalIgnoreCase) &&
                       cacheControl.Contains("no-store", StringComparison.OrdinalIgnoreCase) &&
                       cacheControl.Contains("max-age=0", StringComparison.OrdinalIgnoreCase) &&
                       pragma.Contains("no-cache", StringComparison.OrdinalIgnoreCase);
            }),
            "A latest/version pointer request omitted the required no-cache request headers.");
    }

    private static void AssertHttpTransferDiagnostics(
        AssetBundleBuildResult build,
        AssetBundleDescriptor changedBundle,
        ConcurrentQueue<AssetBundleHttpTransferDiagnostic> transfers)
    {
        var snapshot = transfers.ToArray();
        TestAssert.That(snapshot.Length >= 5,
            "HTTP transfer diagnostics did not include retry and successful response records.");
        TestAssert.That(snapshot.All(item =>
                !item.Endpoint.Contains("diagnostic-user", StringComparison.Ordinal) &&
                !item.Endpoint.Contains("diagnostic-password", StringComparison.Ordinal) &&
                !item.Endpoint.Contains("access_token", StringComparison.Ordinal) &&
                !item.Endpoint.Contains('#', StringComparison.Ordinal)),
            "HTTP transfer diagnostics exposed URI credentials, query data, or fragments.");

        // This compatibility fixture deliberately serves the legacy complete version manifest.
        var latestBytes = new FileInfo(Path.Combine(build.VersionDirectory, "version.json")).Length;
        var failedLatest = snapshot.Single(item =>
            item.ResourceKind == AssetBundleHttpResourceKind.VersionPointer &&
            item.Result != NetworkRequestResult.Success);
        TestAssert.That(failedLatest.Attempt == 1 && failedLatest.StatusCode is null &&
                        failedLatest.ReceivedBytes == 0,
            "The failed version pointer attempt was missing from HTTP transfer diagnostics.");
        var latest = snapshot.Single(item =>
            item.ResourceKind == AssetBundleHttpResourceKind.VersionPointer &&
            item.Result == NetworkRequestResult.Success);
        TestAssert.That(latest.Endpoint == "https://asset-bundle.test/content/latest.json" &&
                        latest.Attempt == 2 && latest.StatusCode == 200 &&
                        latest.ReceivedBytes == latestBytes,
            "The version pointer diagnostic did not report its redacted endpoint, retry, status, and actual bytes.");

        var catalogBytes = new FileInfo(Path.Combine(build.VersionDirectory, "catalog.json")).Length;
        var catalog = snapshot.Single(item =>
            item.ResourceKind == AssetBundleHttpResourceKind.Catalog &&
            item.Result == NetworkRequestResult.Success);
        TestAssert.That(catalog.Endpoint ==
                        $"https://asset-bundle.test/content/{build.Version.Version}/catalog.json" &&
                        catalog.Attempt == 1 && catalog.StatusCode == 200 &&
                        catalog.ReceivedBytes == catalogBytes,
            "The catalog diagnostic did not report its redacted endpoint, status, and actual bytes.");

        var failedBundle = snapshot.Single(item =>
            item.ResourceKind == AssetBundleHttpResourceKind.Bundle &&
            item.Result != NetworkRequestResult.Success);
        TestAssert.That(failedBundle.Attempt == 1 && failedBundle.StatusCode is null &&
                        failedBundle.ReceivedBytes == 0,
            "The failed bundle attempt was missing from HTTP transfer diagnostics.");
        var bundle = snapshot.Single(item =>
            item.ResourceKind == AssetBundleHttpResourceKind.Bundle &&
            item.Result == NetworkRequestResult.Success);
        TestAssert.That(bundle.Endpoint.EndsWith(
                            "/" + changedBundle.FileName, StringComparison.Ordinal) &&
                        bundle.Attempt == 2 && bundle.StatusCode == 200 &&
                        bundle.ReceivedBytes == changedBundle.Size,
            "The bundle diagnostic did not report its redacted endpoint, retry, status, and actual bytes.");
    }

    private static string RemotePath(params string[] segments) =>
        "/content/" + string.Join('/', segments);
}
