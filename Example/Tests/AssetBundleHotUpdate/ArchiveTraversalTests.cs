using System.IO.Compression;
using System.Text;
using BEngine.AssetBundles;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class ArchiveTraversalTests
{
    internal static async Task RunAsync(AssetBundleTestWorkspace workspace)
    {
        var packageDirectory = Path.Combine(workspace.Root, "MaliciousRemote",
            AssetBundleTestWorkspace.PackageName);
        var versionDirectory = Path.Combine(packageDirectory, "1.0.0");
        var bundleDirectory = Path.Combine(versionDirectory, "bundles");
        Directory.CreateDirectory(bundleDirectory);
        var temporaryBundle = Path.Combine(bundleDirectory, "payload.tmp");
        var safeBytes = Encoding.UTF8.GetBytes("safe");
        await using (var output = new FileStream(temporaryBundle, FileMode.CreateNew, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
        {
            await WriteEntry(archive, "Assets/Data/safe.txt", safeBytes).ConfigureAwait(false);
            await WriteEntry(archive, "../escaped.txt", Encoding.UTF8.GetBytes("escaped"))
                .ConfigureAwait(false);
        }
        var bundleBytes = await File.ReadAllBytesAsync(temporaryBundle).ConfigureAwait(false);
        var bundleHash = AssetBundleCatalogSerializer.ComputeSha256(bundleBytes);
        var bundleFileName = bundleHash + ".bassetbundle";
        File.Move(temporaryBundle, Path.Combine(bundleDirectory, bundleFileName));
        var catalog = new AssetBundleCatalog
        {
            PackageName = AssetBundleTestWorkspace.PackageName,
            Version = "1.0.0",
            Bundles =
            [
                new AssetBundleDescriptor
                {
                    Name = "malicious",
                    FileName = bundleFileName,
                    Sha256 = bundleHash,
                    Size = bundleBytes.LongLength
                }
            ],
            Assets =
            [
                new AssetBundleAsset
                {
                    Address = "Assets/Data/safe.txt",
                    Guid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Bundle = "malicious",
                    Entry = "Assets/Data/safe.txt",
                    AssetType = "BEngine.TextAsset",
                    Sha256 = AssetBundleCatalogSerializer.ComputeSha256(safeBytes),
                    Size = safeBytes.LongLength
                }
            ]
        };
        var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(catalog);
        var version = new AssetBundleVersion
        {
            PackageName = AssetBundleTestWorkspace.PackageName,
            Version = "1.0.0",
            CatalogFile = "catalog.json",
            CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes),
            CatalogSize = catalogBytes.LongLength
        };
        await File.WriteAllBytesAsync(Path.Combine(versionDirectory, "catalog.json"), catalogBytes)
            .ConfigureAwait(false);
        var versionBytes = AssetBundleCatalogSerializer.SerializeVersion(version);
        await File.WriteAllBytesAsync(Path.Combine(versionDirectory, "version.json"), versionBytes)
            .ConfigureAwait(false);
        await File.WriteAllBytesAsync(Path.Combine(packageDirectory, "latest.json"), versionBytes)
            .ConfigureAwait(false);

        var escapedPath = Path.Combine(packageDirectory, "escaped.txt");
        await using var manager = RuntimeAssetBundleTests.CreateManager(
            Path.Combine(workspace.Root, "Caches", "malicious-archive"), packageDirectory);
        await manager.InitializeAsync().ConfigureAwait(false);
        await TestAssert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var ignored = await manager.LoadBytesAsync("Assets/Data/safe.txt").ConfigureAwait(false);
        }, "unexpected asset bundle entry").ConfigureAwait(false);
        TestAssert.That(!File.Exists(escapedPath),
            "A path-traversal archive entry created a file outside bundle storage.");
    }

    private static async Task WriteEntry(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        await using var destination = entry.Open();
        await destination.WriteAsync(bytes).ConfigureAwait(false);
    }
}
