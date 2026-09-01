using System.IO.Compression;
using BEngine.AssetBundles;
using BEngine.Editor;
using BEngine.ProjectSystem.Editor;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class ArtifactBundleBoundaryTests
{
    internal static void Run(AssetBundleTestWorkspace workspace)
    {
        SourceChangesDoNotReplaceTheImportedArtifact(workspace);
        ChangedArtifactIsRejectedAgainstTheDatabaseSnapshot(workspace);
        MissingArtifactIsRejected(workspace);
    }

    private static void SourceChangesDoNotReplaceTheImportedArtifact(AssetBundleTestWorkspace workspace)
    {
        var record = GetSharedRecord(workspace);
        var expectedArtifact = File.ReadAllBytes(record.ArtifactPath);
        File.WriteAllText(record.SourcePath, "source-modified-without-import");
        try
        {
            var build = BuildShared(workspace, "artifact-boundary-source", "artifact-source");
            var catalogAsset = build.Catalog.Assets.Single(asset =>
                asset.Address == AssetBundleTestWorkspace.SharedAddress);
            TestAssert.That(catalogAsset.Sha256 == record.ArtifactHash &&
                            catalogAsset.Size == record.ArtifactSize,
                "The bundle catalog did not use the AssetDatabase artifact hash and size.");
            TestAssert.That(ReadPayload(build, catalogAsset).AsSpan().SequenceEqual(expectedArtifact),
                "The bundle payload was read from Source instead of the imported Artifact.");
        }
        finally
        {
            File.WriteAllBytes(record.SourcePath, expectedArtifact);
        }
    }

    private static void ChangedArtifactIsRejectedAgainstTheDatabaseSnapshot(
        AssetBundleTestWorkspace workspace)
    {
        var record = GetSharedRecord(workspace);
        var original = File.ReadAllBytes(record.ArtifactPath);
        var changed = original.ToArray();
        changed[0] ^= 0x5a;
        File.WriteAllBytes(record.ArtifactPath, changed);
        try
        {
            _ = TestAssert.Throws<InvalidDataException>(
                () => BuildShared(workspace, "artifact-boundary-changed", "artifact-changed"),
                "hash differs from the AssetDatabase snapshot");
        }
        finally
        {
            File.WriteAllBytes(record.ArtifactPath, original);
        }
    }

    private static void MissingArtifactIsRejected(AssetBundleTestWorkspace workspace)
    {
        var record = GetSharedRecord(workspace);
        var original = File.ReadAllBytes(record.ArtifactPath);
        File.Delete(record.ArtifactPath);
        try
        {
            _ = TestAssert.Throws<FileNotFoundException>(
                () => BuildShared(workspace, "artifact-boundary-missing", "artifact-missing"),
                "artifact does not exist");
        }
        finally
        {
            File.WriteAllBytes(record.ArtifactPath, original);
        }
    }

    private static AssetRecord GetSharedRecord(AssetBundleTestWorkspace workspace) =>
        workspace.AssetDatabase.GetRecord(AssetBundleTestWorkspace.SharedAddress) ??
        throw new InvalidOperationException("The shared AssetDatabase record is missing.");

    private static AssetBundleBuildResult BuildShared(
        AssetBundleTestWorkspace workspace,
        string version,
        string outputName) =>
        AssetBundleBuilder.Build(
            workspace.Workspace,
            workspace.AssetDatabase,
            [new AssetBundleBuildDefinition
            {
                Name = "main",
                AssetPaths = [AssetBundleTestWorkspace.SharedAddress]
            }],
            new AssetBundleBuildOptions
            {
                PackageName = AssetBundleTestWorkspace.PackageName,
                Version = version,
                OutputDirectory = Path.Combine(workspace.Root, "Builds", outputName)
            });

    private static byte[] ReadPayload(AssetBundleBuildResult build, AssetBundleAsset asset)
    {
        var bundle = build.Catalog.Bundles.Single(item => item.Name == asset.Bundle);
        using var stream = File.OpenRead(Path.Combine(build.VersionDirectory, "bundles", bundle.FileName));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(asset.Entry) ??
                    throw new InvalidDataException($"Bundle entry '{asset.Entry}' is missing.");
        using var payload = entry.Open();
        using var bytes = new MemoryStream();
        payload.CopyTo(bytes);
        return bytes.ToArray();
    }
}
