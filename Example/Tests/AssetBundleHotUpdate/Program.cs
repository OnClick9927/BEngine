namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class Program
{
    private static async Task<int> Main()
    {
        using var workspace = new AssetBundleTestWorkspace();
        try
        {
            CatalogValidationTests.Run();
            CatalogSerializationTests.Run();
            var builds = DeterministicBundleBuildTests.Run(workspace);
            await RuntimeAssetBundleTests.RunAsync(workspace, builds.VersionOne).ConfigureAwait(false);
            await ArchiveTraversalTests.RunAsync(workspace).ConfigureAwait(false);
            await RemoteUpdateTests.RunAsync(workspace, builds).ConfigureAwait(false);
            Console.WriteLine(
                "ASSET_BUNDLE_HOT_UPDATE_OK|catalog,deterministic-manifest,deterministic-bundle,strict-json," +
                "dependencies,cycles,content-addressing,path-traversal,archive-entry-safety,async-load,cache,refcount,unload," +
                "remote-version,retry,hash-verification,atomic-activation,rollback,offline-cache,staging-cleanup," +
                "resources-provider,player-scene-priority,bundled-sprite-import,bundled-sprite-render");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ASSET_BUNDLE_HOT_UPDATE_FAILED|{exception}");
            return 1;
        }
    }
}
