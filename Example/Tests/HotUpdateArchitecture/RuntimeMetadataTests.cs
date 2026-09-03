using BEngine.Build;
using BEngine.Content;
using BEngine.ProjectSystem;

namespace BEngine.ExampleTests.HotUpdateArchitecture;

internal static class RuntimeMetadataTests
{
    internal static void Run()
    {
        var document = new RuntimeMetadataDocument
        {
            Project = new ProjectData { Name = "Binary Metadata Test" },
            ProjectSettings = new ProjectSettingsData
            {
                CompanyName = "BEngine",
                ProductName = "Binary Metadata Test"
            },
            AssetBundles = new BEngine.AssetBundles.AssetBundleSettingsDocument
            {
                Enabled = true,
                PackageName = "binary-test",
                BuiltInDirectory = "AssetBundles",
                RequireHttps = false,
                RemoteBaseUrl = "http://127.0.0.1:18080/content"
            },
            EnabledPackages =
            [
                new RuntimePackageReferenceData
                {
                    Id = "com.bengine.tests.runtime-metadata",
                    Version = "2.0.0"
                }
            ],
            PlayerBootstrap = new PlayerBootstrapManifest
            {
                ProductName = "Binary Metadata Test",
                BuildVersion = "2.0.0",
                Executable = "binary metadata test.exe",
                DataDirectory = "binary metadata test_data",
                ResourceDirectory = "binary metadata test_data",
                AssemblyDirectory = "binary metadata test_data/assembly",
                PlayerResourceArchive = "binary metadata test_data/resources/player.bresources",
                BuildTargetResource = PlayerPackagedResourceAddresses.BuildTargetManifest,
                SplashScreenEnabled = true,
                SplashImageResource = PlayerPackagedResourceAddresses.SplashImage,
                SplashBackgroundColor = "#112233",
                SplashMinimumDurationSeconds = 1.25f,
                CacheDirectory = "cache/live",
                HotUpdateStartupScene = "Assets/Scenes/Main.scene.yaml"
            }
        };

        var bytes = RuntimeMetadataSerializer.Serialize(document);
        var roundTrip = RuntimeMetadataSerializer.Deserialize(bytes);
        Require(roundTrip.Project.Name == document.Project.Name &&
                roundTrip.ProjectSettings.ProductName == document.ProjectSettings.ProductName &&
                roundTrip.AssetBundles.RemoteBaseUrl == document.AssetBundles.RemoteBaseUrl &&
                roundTrip.PlayerBootstrap?.Executable == "binary metadata test.exe" &&
                roundTrip.PlayerBootstrap.PlayerResourceArchive.EndsWith(
                    "resources/player.bresources", StringComparison.Ordinal) &&
                roundTrip.PlayerBootstrap.SplashImageResource ==
                    PlayerPackagedResourceAddresses.SplashImage &&
                roundTrip.PlayerBootstrap.SplashBackgroundColor == "#112233" &&
                roundTrip.PlayerBootstrap.SplashMinimumDurationSeconds == 1.25f &&
                roundTrip.PlayerBootstrap.CacheDirectory == "cache/live" &&
                roundTrip.PlayerBootstrap.HotUpdateStartupScene == "Assets/Scenes/Main.scene.yaml" &&
                roundTrip.EnabledPackages is [{ Id: "com.bengine.tests.runtime-metadata", Version: "2.0.0" }],
            "Runtime metadata binary round trip lost project, settings, AssetBundle, package, or Player data.");

        var corrupted = bytes.ToArray();
        corrupted[^1] ^= 0x5a;
        RequireThrows<InvalidDataException>(() => RuntimeMetadataSerializer.Deserialize(corrupted),
            "Runtime metadata accepted a payload with a broken SHA-256 integrity hash.");
        RequireThrows<InvalidDataException>(() => RuntimeMetadataSerializer.Deserialize(bytes[..^1]),
            "Runtime metadata accepted a truncated payload.");
        RequireThrows<InvalidDataException>(
            () => PlayerBootstrapManifest.NormalizeCacheDirectory("../outside"),
            "Player bootstrap accepted a cache path that escapes the package root.");
        RequireThrows<InvalidDataException>(
            () => PlayerBootstrapManifest.NormalizeCacheDirectory("C:\\outside"),
            "Player bootstrap accepted an absolute Windows cache path.");
        document.PlayerBootstrap!.HotUpdateStartupScene = "../Scenes/Main.scene.yaml";
        RequireThrows<InvalidDataException>(() => RuntimeMetadataSerializer.Serialize(document),
            "Player bootstrap accepted a HotUpdate startup scene outside Assets.");
        document.PlayerBootstrap.HotUpdateStartupScene = "Assets/Scenes/Main.scene.yaml";
        document.PlayerBootstrap.CacheDirectory = document.PlayerBootstrap.DataDirectory;
        RequireThrows<InvalidDataException>(() => RuntimeMetadataSerializer.Serialize(document),
            "Player bootstrap accepted a cache directory that overlaps immutable Player data.");
        document.PlayerBootstrap.CacheDirectory = "cache/live";

        var root = Path.Combine(Path.GetTempPath(), $"BEngineRuntimeMetadata.{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var resources = Path.Combine(
                root, PlayerPackagedResourceAddresses.ResourcesDirectoryName);
            Directory.CreateDirectory(resources);
            BuiltInResourceArchive.Write(
                Path.Combine(resources, PlayerPackagedResourceAddresses.PlayerArchiveFileName),
                [BuiltInResourceArchiveWriteEntry.FromMemory(
                    PlayerPackagedResourceAddresses.RuntimeMetadata,
                    bytes,
                    "PlayerRuntimeMetadata")]);
            var workspace = ProjectWorkspace.OpenRuntime(root);
            Require(workspace.RuntimeMetadata is not null &&
                    workspace.Project.Name == "Binary Metadata Test" &&
                    !Directory.Exists(workspace.AssetsPath),
                "Runtime workspace did not open binary metadata without creating source directories.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RequireThrows<TException>(Action action, string message)
        where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }
}
