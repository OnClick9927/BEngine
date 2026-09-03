namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class Program
{
    private static async Task<int> Main()
    {
        using var workspace = new AssetBundleTestWorkspace();
        try
        {
            TrimSafeJsonSerializationTests.Run();
            await BuiltInResourceArchiveTests.RunAsync().ConfigureAwait(false);
            CatalogValidationTests.Run();
            CatalogSerializationTests.Run();
            ArtifactBundleBoundaryTests.Run(workspace);
            var builds = DeterministicBundleBuildTests.Run(workspace);
            await CatalogIndexConcurrencyTests.RunAsync(workspace, builds.VersionOne).ConfigureAwait(false);
            await RuntimeAssetBundleTests.RunAsync(workspace, builds.VersionOne).ConfigureAwait(false);
            await ArchiveTraversalTests.RunAsync(workspace).ConfigureAwait(false);
            await RemoteUpdateTests.RunAsync(workspace, builds).ConfigureAwait(false);
            Console.WriteLine(
                "ASSET_BUNDLE_HOT_UPDATE_OK|trim-safe-json,built-in-resource-archive,catalog,deterministic-manifest,deterministic-bundle,strict-json," +
                "artifact-input,artifact-snapshot,artifact-catalog-integrity," +
                "library-file-subasset,subasset-owner-localid,guid-subasset-runtime," +
                "dependencies,cycles,content-addressing,opaque-compressed-entries,catalog-index,index-performance,concurrent-init-load," +
                "path-traversal,archive-entry-safety,async-load,cache,refcount,unload," +
                "remote-version,retry,hash-verification,immutable-update-plan,atomic-activation,rollback," +
                "remote-authoritative-reverse-version,minimal-latest-pointer,version-metadata-validation," +
                "no-cache-version-pointer,latest-required," +
                "offline-cache,staging-cleanup," +
                "resources-provider,versioned-object-cache,player-scene-priority," +
                "bundled-main-assets,generic-bundled-references," +
                "bundled-sprite-import,bundled-sprite-render");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ASSET_BUNDLE_HOT_UPDATE_FAILED|{exception}");
            return 1;
        }
    }
}
