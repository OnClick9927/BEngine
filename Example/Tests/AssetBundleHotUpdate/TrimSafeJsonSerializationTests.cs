using System.Text;
using System.Text.Json;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.HotUpdate;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class TrimSafeJsonSerializationTests
{
    internal static void Run()
    {
        TestAssert.That(!JsonSerializer.IsReflectionEnabledByDefault,
            "The trim-safe JSON regression process did not disable reflection serialization.");
        PlayerBootstrapRoundTrips();
        BuildTargetRoundTripsWithStringEnums();
        AssetBundleMetadataRoundTrips();
        ManagedCodeReleaseRoundTrips();
    }

    private static void PlayerBootstrapRoundTrips()
    {
        var source = new PlayerBootstrapManifest
        {
            ProductName = "Trim Safe Player",
            BuildVersion = "2.1.0",
            DevelopmentBuild = false,
            WritePlayerLog = true,
            Executable = "trim safe player.exe",
            DataDirectory = "trim safe player_data",
            ResourceDirectory = "trim safe player_data",
            AssemblyDirectory = "trim safe player_data/assembly",
            PlayerResourceArchive = "trim safe player_data/resources/player.bresources",
            BuildTargetResource = PlayerPackagedResourceAddresses.BuildTargetManifest,
            SplashScreenEnabled = true,
            SplashImageResource = PlayerPackagedResourceAddresses.SplashImage,
            SplashBackgroundColor = "#102030",
            SplashMinimumDurationSeconds = 1.25f,
            CacheDirectory = "cache/trim-safe",
            HotUpdateStartupScene = "Assets/Scenes/Main.scene.yaml"
        };
        var restored = PlayerBootstrapManifestSerializer.Deserialize(
            PlayerBootstrapManifestSerializer.Serialize(source));
        TestAssert.That(restored.ProductName == source.ProductName &&
                        restored.ResourceDirectory == source.ResourceDirectory &&
                        restored.PlayerResourceArchive == source.PlayerResourceArchive &&
                        restored.BuildTargetResource == source.BuildTargetResource &&
                        restored.WritePlayerLog &&
                        restored.SplashScreenEnabled &&
                        restored.SplashImageResource == source.SplashImageResource &&
                        restored.SplashBackgroundColor == source.SplashBackgroundColor &&
                        restored.SplashMinimumDurationSeconds == source.SplashMinimumDurationSeconds &&
                        restored.CacheDirectory == source.CacheDirectory &&
                        restored.HotUpdateStartupScene == source.HotUpdateStartupScene,
            "Player bootstrap manifest did not round-trip without reflection serialization.");
    }

    private static void BuildTargetRoundTripsWithStringEnums()
    {
        var source = BuildTargetManifest.FromDescriptor(BuildTargetCatalog.Get("windows-x64"));
        var bytes = BuildTargetManifestSerializer.Serialize(source);
        var restored = BuildTargetManifestSerializer.Deserialize(bytes);
        var json = Encoding.UTF8.GetString(bytes);
        TestAssert.That(restored.TargetId == source.TargetId &&
                        restored.Platform == source.Platform &&
                        restored.GraphicsBackends.SequenceEqual(source.GraphicsBackends) &&
                        json.Contains("\"platform\": \"Windows\"", StringComparison.Ordinal),
            "Build-target manifest lost its source-generated string-enum contract.");
    }

    private static void AssetBundleMetadataRoundTrips()
    {
        var catalog = CatalogValidationTests.CreateCatalog();
        var restoredCatalog = AssetBundleCatalogSerializer.DeserializeCatalog(
            AssetBundleCatalogSerializer.SerializeCatalog(catalog));
        var version = CatalogValidationTests.CreateVersion();
        var restoredVersion = AssetBundleCatalogSerializer.DeserializeVersion(
            AssetBundleCatalogSerializer.SerializeVersion(version));
        var latest = new AssetBundleLatestPointer
        {
            PackageName = version.PackageName,
            Version = version.Version
        };
        var restoredLatest = AssetBundleCatalogSerializer.DeserializeLatestPointer(
            AssetBundleCatalogSerializer.SerializeLatestPointer(latest));
        TestAssert.That(restoredCatalog.Assets.Count == catalog.Assets.Count &&
                        restoredCatalog.SchemaVersion == AssetBundleCatalog.CurrentSchemaVersion &&
                        restoredVersion.CatalogSha256 == version.CatalogSha256 &&
                        restoredLatest.Version == version.Version,
            "AssetBundle catalog/version/latest metadata did not round-trip without reflection serialization.");
    }

    private static void ManagedCodeReleaseRoundTrips()
    {
        var source = new ManagedCodeReleaseManifest
        {
            ReleaseId = "trim-safe-release",
            Modules =
            [
                new ManagedCodeModuleManifest
                {
                    Name = "Game",
                    BuildId = "build-1",
                    AssemblyAddress = "Assets/__BEngine/HotUpdate/Game.dll",
                    SymbolsAddress = null
                }
            ]
        };
        var restored = ManagedCodeReleaseManifestSerializer.Deserialize(
            ManagedCodeReleaseManifestSerializer.Serialize(source));
        TestAssert.That(restored.ReleaseId == source.ReleaseId &&
                        restored.Modules.Single().SymbolsAddress is null,
            "Managed-code release manifest did not round-trip without reflection serialization.");
    }
}
