using BEngine.AssetBundles;
using BEngine.Documents;
using BEngine.Editor;
using BEngine.Player;
using BEngine.Rendering;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class RuntimeAssetBundleTests
{
    internal static async Task RunAsync(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult build)
    {
        await SerialLoadsAreCachedAndReferenceCounted(workspace, build).ConfigureAwait(false);
        await ResourcesAndPlayerPreferActiveBundles(workspace, build).ConfigureAwait(false);
        await ValidOfflineCacheSurvivesRestartAndRecoversPointer(workspace, build).ConfigureAwait(false);
    }

    private static async Task SerialLoadsAreCachedAndReferenceCounted(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult build)
    {
        var cache = Path.Combine(workspace.Root, "Caches", "runtime-loads");
        await using var manager = CreateManager(cache, build.PackageDirectory);
        await manager.InitializeAsync().ConfigureAwait(false);
        TestAssert.That(manager.IsInitialized && manager.ActiveVersion?.Version == "1.0.0",
            "The built-in catalog was not selected during initialization.");
        TestAssert.That(manager.EnumerateAddresses("Data").SequenceEqual(
                new[] { AssetBundleTestWorkspace.BinaryAddress, AssetBundleTestWorkspace.SharedAddress },
                StringComparer.OrdinalIgnoreCase),
            "Address prefix enumeration did not return stable matching assets.");

        var handles = new AssetBundleHandle<byte[]>[8];
        for (var index = 0; index < handles.Length; index++)
            handles[index] = await manager.LoadBytesAsync(AssetBundleTestWorkspace.SharedAddress)
                .ConfigureAwait(false);
        TestAssert.That(handles.All(handle =>
                System.Text.Encoding.UTF8.GetString(handle.Value) == "shared-v1"),
            "Serial async loads returned incorrect bytes.");
        TestAssert.That(handles.Skip(1).All(handle => !ReferenceEquals(handles[0].Value, handle.Value)),
            "Asset handles exposed the cache's mutable byte array instance.");
        TestAssert.That(manager.UnloadUnused() == 0,
            "A bundle was unloaded while live handles still referenced it.");
        foreach (var handle in handles[..^1]) handle.Dispose();
        TestAssert.That(manager.UnloadUnused() == 0,
            "A shared bundle was unloaded before its final handle was released.");
        handles[^1].Dispose();
        handles[^1].Dispose();
        TestAssert.That(manager.UnloadUnused() == 1,
            "The shared bundle was not unloaded exactly once after its final release.");

        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await TestAssert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                using var ignored = await manager.LoadBytesAsync(
                    AssetBundleTestWorkspace.BinaryAddress, cancellation.Token).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        await using (var recovered = await manager.LoadBytesAsync(
                         AssetBundleTestWorkspace.BinaryAddress).ConfigureAwait(false))
            TestAssert.That(recovered.Value.SequenceEqual(new byte[] { 0, 1, 2, 3, 4, 255 }),
                "A cancelled load poisoned the bundle cache for the next caller.");
        TestAssert.That(manager.UnloadUnused() == 1,
            "The recovered load left its shared bundle cached after release.");

        await using (var scene = await manager.LoadTextAsync(
                         AssetBundleTestWorkspace.SceneAddress).ConfigureAwait(false))
        {
            var document = Document.FromYaml<SceneDocument>(scene.Value);
            TestAssert.That(document.Name == "Bundled Scene",
                "Text loading did not decode the bundled scene payload.");
            TestAssert.That(manager.UnloadUnused() == 0,
                "A dependency or owner bundle unloaded while the scene handle was alive.");
        }
        TestAssert.That(manager.UnloadUnused() == 2,
            "Releasing a dependent asset did not make both owner and dependency unloadable.");

        TestAssert.That(manager.TryLoadBytes("Data/shared.txt", out var synchronous) &&
                        System.Text.Encoding.UTF8.GetString(synchronous) == "shared-v1",
            "TryLoadBytes did not support a canonicalizable address.");
        TestAssert.That(manager.UnloadUnused() == 1,
            "TryLoadBytes leaked its internal handle.");
        await TestAssert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var ignored = await manager.LoadBytesAsync("../outside.txt").ConfigureAwait(false);
        }, "asset address").ConfigureAwait(false);
    }

    private static async Task ResourcesAndPlayerPreferActiveBundles(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult build)
    {
        var cache = Path.Combine(workspace.Root, "Caches", "host-integration");
        await using var manager = CreateManager(cache, build.PackageDirectory);
        await manager.InitializeAsync().ConfigureAwait(false);
        var provider = new AssetBundleResourceProvider(manager);
        var diskResource = Path.Combine(workspace.Workspace.AssetsPath, "Resources", "config.txt");
        await File.WriteAllTextAsync(diskResource, "resource-from-disk-after-build").ConfigureAwait(false);
        BEngine.Resources.RegisterResourceRoot(workspace.Workspace.AssetsPath);
        BEngine.Resources.RegisterResourceProvider(provider);
        try
        {
            TestAssert.That(BEngine.Resources.Load<string>("config") == "resource-v1",
                "Resources did not prefer the active AssetBundle provider over a disk resource.");
            TestAssert.That(BEngine.Resources.LoadAll<string>().Contains("resource-v1", StringComparer.Ordinal),
                "Resources.LoadAll did not enumerate active AssetBundle content.");
            var bundledSpriteAsset = build.Catalog.Assets.Single(asset =>
                asset.Address == AssetBundleTestWorkspace.SpriteAddress);
            TestAssert.That(bundledSpriteAsset.Importer == "TextureImporter" &&
                            bundledSpriteAsset.ImporterSettings.GetValueOrDefault("textureType") == "Sprite" &&
                            bundledSpriteAsset.ImporterSettings.GetValueOrDefault("spritePivotX") == "0.25" &&
                            bundledSpriteAsset.ImporterSettings.GetValueOrDefault("spritePivotY") == "0.75",
                "The bundle catalog did not preserve the Sprite importer description.");

            workspace.WriteScene("Disk Scene", "Disk Root", includeBundledSprite: false);
            workspace.DeleteSpriteSource();
            var services = new ServiceCollection();
            services.AddSingleton<IAssetBundleManager>(manager);
            services.AddBEnginePlayer(workspace.Workspace.RootPath);
            using var serviceProvider = services.BuildServiceProvider();
            var loader = serviceProvider.GetRequiredService<ISceneLoader>();
            var scene = loader.LoadScene("main", serviceProvider);
            try
            {
                var sprite = scene.QueryComponents<SpriteRenderer>().Single().sprite;
                TestAssert.That(scene.name == "Bundled Scene" &&
                                scene.path == AssetBundleTestWorkspace.SceneAddress &&
                                scene.gameObjects.Any(item => item.name == "Bundled Root"),
                    "The Player scene loader did not prefer the active bundle over the project scene file.");
                TestAssert.That(sprite is not null &&
                                sprite.assetPath == AssetBundleTestWorkspace.SpriteAddress &&
                                sprite.Texture == "@bundle/" + AssetBundleTestWorkspace.SpriteAddress &&
                                Math.Abs((double)sprite.pivot.x - 0.25) < 0.0001 &&
                                Math.Abs((double)sprite.pivot.y - 0.75) < 0.0001,
                    "The bundled Scene did not restore its SpriteRenderer from bundle importer metadata.");
                TestAssert.That(BEngine.Resources.Load<byte[]>(sprite!.Texture) is { } bundledBytes &&
                                bundledBytes.AsSpan().SequenceEqual(workspace.SpriteBytes),
                    "The Sprite texture did not resolve to the active bundle after its project source was deleted.");

                workspace.WriteStaleSpriteSource();
                var engineResourceRoot = Path.Combine(FindRepositoryRoot(), "src", "Core");
                BEngine.Resources.RegisterResourceRoot(engineResourceRoot);
                try
                {
                    using var graphics = new RecordingBundleGraphicsDevice();
                    using var renderer = new PortableSceneRenderer(graphics);
                    renderer.Render(scene, RenderCamera.Default, 128, 128,
                        drawGrid: false, drawUi: false, drawExtensions: false);
                    TestAssert.That(graphics.Textures.Any(texture =>
                                            texture.Label.EndsWith("bundled.png", StringComparison.OrdinalIgnoreCase) &&
                                            texture.Description.Width == 2 && texture.Description.Height == 2 &&
                                            texture.InitialData.Length == 16) &&
                                    graphics.DrawCount > 0,
                        "The renderer did not decode and draw the Sprite texture from bundle bytes.");
                }
                finally
                {
                    _ = BEngine.Resources.UnregisterResourceRoot(engineResourceRoot);
                }
            }
            finally
            {
                scene.Dispose();
            }
        }
        finally
        {
            _ = BEngine.Resources.UnregisterResourceProvider(provider);
            _ = BEngine.Resources.UnregisterResourceRoot(workspace.Workspace.AssetsPath);
        }
        _ = manager.UnloadUnused();
    }

    private static async Task ValidOfflineCacheSurvivesRestartAndRecoversPointer(
        AssetBundleTestWorkspace workspace,
        AssetBundleBuildResult build)
    {
        var cache = Path.Combine(workspace.Root, "Caches", "offline");
        var objects = Path.Combine(cache, "objects");
        var catalogs = Path.Combine(cache, "catalogs");
        var staging = Path.Combine(cache, "staging");
        Directory.CreateDirectory(objects);
        Directory.CreateDirectory(catalogs);
        Directory.CreateDirectory(staging);
        foreach (var descriptor in build.Catalog.Bundles)
            File.Copy(
                Path.Combine(build.VersionDirectory, "bundles", descriptor.FileName),
                Path.Combine(objects, descriptor.FileName));
        File.Copy(
            Path.Combine(build.VersionDirectory, "catalog.json"),
            Path.Combine(catalogs, build.Version.CatalogSha256 + ".json"));
        var versionBytes = AssetBundleCatalogSerializer.SerializeVersion(build.Version);
        await File.WriteAllBytesAsync(Path.Combine(cache, "previous.json"), versionBytes).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(cache, "active.json"), "{ corrupt json")
            .ConfigureAwait(false);
        var abandonedPart = Path.Combine(staging, "abandoned.part");
        await File.WriteAllTextAsync(abandonedPart, "partial download").ConfigureAwait(false);

        await using var manager = CreateManager(cache, builtInDirectory: null);
        await manager.InitializeAsync().ConfigureAwait(false);
        TestAssert.That(manager.ActiveVersion?.Version == build.Version.Version &&
                        !File.Exists(abandonedPart),
            "Offline initialization did not recover the previous valid cache or clean staging.");
        await using (var handle = await manager.LoadTextAsync(
                         AssetBundleTestWorkspace.SharedAddress).ConfigureAwait(false))
            TestAssert.That(handle.Value == "shared-v1",
                "A valid offline cache could not serve assets without a remote source.");

        var orphanObject = Path.Combine(objects,
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.bassetbundle");
        var orphanCatalog = Path.Combine(catalogs,
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb.json");
        await File.WriteAllTextAsync(orphanObject, "orphan").ConfigureAwait(false);
        await File.WriteAllTextAsync(orphanCatalog, "orphan").ConfigureAwait(false);
        var deleted = await manager.CleanupAsync().ConfigureAwait(false);
        TestAssert.That(deleted == 2 && !File.Exists(orphanObject) && !File.Exists(orphanCatalog),
            "Cache cleanup did not delete unreferenced objects and catalogs.");
        TestAssert.That(build.Catalog.Bundles.All(descriptor =>
                File.Exists(Path.Combine(objects, descriptor.FileName))) &&
                        File.Exists(Path.Combine(catalogs, build.Version.CatalogSha256 + ".json")),
            "Cache cleanup removed active content.");
    }

    internal static AssetBundleManager CreateManager(string cache, string? builtInDirectory, HttpClient? http = null) =>
        new(new AssetBundleRuntimeOptions
        {
            PackageName = AssetBundleTestWorkspace.PackageName,
            CacheDirectory = cache,
            BuiltInDirectory = builtInDirectory,
            HttpClient = http,
            RemoteBaseUri = http is null ? null : new Uri("https://asset-bundle.test/content/"),
            MaxRetries = 2
        });

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "BEngine.sln")))
                return directory.FullName;
        throw new DirectoryNotFoundException("BEngine repository root was not found.");
    }
}
