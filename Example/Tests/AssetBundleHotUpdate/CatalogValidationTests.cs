using BEngine.AssetBundles;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class CatalogValidationTests
{
    private const string SharedBundleHash =
        "1111111111111111111111111111111111111111111111111111111111111111";
    private const string MainBundleHash =
        "2222222222222222222222222222222222222222222222222222222222222222";
    private const string ConfigAssetHash =
        "3333333333333333333333333333333333333333333333333333333333333333";
    private const string SceneAssetHash =
        "4444444444444444444444444444444444444444444444444444444444444444";

    internal static void Run()
    {
        ValidCatalogAndVersionAreAccepted();
        DependenciesMustExistAndRemainAcyclic();
        IdentityAndContentMetadataMustBeUniqueAndValid();
        BundleFilesMustBeContentAddressedLeafNames();
        AssetPayloadPathsMustBeCanonicalAndContained();
        VersionCatalogPathsMustBeCanonicalAndContained();
    }

    private static void ValidCatalogAndVersionAreAccepted()
    {
        CreateCatalog().Validate();
        CreateVersion().Validate();
    }

    private static void DependenciesMustExistAndRemainAcyclic()
    {
        var missing = CreateCatalog();
        missing.Bundles[1].Dependencies = ["missing"];
        TestAssert.Throws<InvalidDataException>(missing.Validate, "missing bundle");

        var self = CreateCatalog();
        self.Bundles[0].Dependencies = ["shared"];
        TestAssert.Throws<InvalidDataException>(self.Validate, "depend on itself");

        var cycle = CreateCatalog();
        cycle.Bundles[0].Dependencies = ["main"];
        cycle.Bundles[1].Dependencies = ["shared"];
        TestAssert.Throws<InvalidDataException>(cycle.Validate, "dependency cycle");

        var duplicate = CreateCatalog();
        duplicate.Bundles[1].Dependencies = ["shared", "SHARED"];
        TestAssert.Throws<InvalidDataException>(duplicate.Validate, "duplicate dependency");
    }

    private static void IdentityAndContentMetadataMustBeUniqueAndValid()
    {
        var duplicateBundle = CreateCatalog();
        duplicateBundle.Bundles[1].Name = "SHARED";
        TestAssert.Throws<InvalidDataException>(duplicateBundle.Validate, "duplicate bundle name");

        var duplicateAddress = CreateCatalog();
        duplicateAddress.Assets[1].Address = "Assets/Data/CONFIG.TXT";
        duplicateAddress.Assets[1].Entry = duplicateAddress.Assets[1].Address;
        TestAssert.Throws<InvalidDataException>(duplicateAddress.Validate, "duplicate asset address");

        var duplicateGuid = CreateCatalog();
        duplicateGuid.Assets[1].Guid = duplicateGuid.Assets[0].Guid;
        TestAssert.Throws<InvalidDataException>(duplicateGuid.Validate, "duplicate asset GUID");

        var invalidHash = CreateCatalog();
        invalidHash.Assets[0].Sha256 = "not-a-sha256";
        TestAssert.Throws<InvalidDataException>(invalidHash.Validate, "64 hexadecimal");

        var missingAssetBundle = CreateCatalog();
        missingAssetBundle.Assets[0].Bundle = "missing";
        TestAssert.Throws<InvalidDataException>(missingAssetBundle.Validate, "missing bundle");
    }

    private static void BundleFilesMustBeContentAddressedLeafNames()
    {
        foreach (var fileName in new[]
                 {
                     $"../{SharedBundleHash}.bassetbundle",
                     $"Bundles/{SharedBundleHash}.bassetbundle",
                     $"{MainBundleHash}.bassetbundle",
                     $"{SharedBundleHash}.zip"
                 })
        {
            var catalog = CreateCatalog();
            catalog.Bundles[0].FileName = fileName;
            TestAssert.Throws<InvalidDataException>(catalog.Validate, "content-addressed name");
        }
    }

    private static void AssetPayloadPathsMustBeCanonicalAndContained()
    {
        foreach (var address in new[]
                 {
                     "../outside.txt",
                     "Assets/../outside.txt",
                     "Assets\\..\\outside.txt",
                     "/outside.txt",
                     "C:/outside.txt",
                     "C:\\outside.txt",
                     "//server/share/outside.txt",
                     "Assets//Data/config.txt",
                     "Assets/./Data/config.txt",
                     "Assets/NUL/config.txt",
                     "Assets/Data/config.txt."
                 })
        {
            var catalog = CreateCatalog();
            catalog.Assets[0].Address = address;
            catalog.Assets[0].Entry = address;
            TestAssert.Throws<InvalidDataException>(catalog.Validate, "asset address");
        }

        var mismatchedEntry = CreateCatalog();
        mismatchedEntry.Assets[0].Entry = "Assets/Data/other.txt";
        TestAssert.Throws<InvalidDataException>(mismatchedEntry.Validate, "canonical payload path");
    }

    private static void VersionCatalogPathsMustBeCanonicalAndContained()
    {
        foreach (var catalogFile in new[]
                 {
                     "../catalog.json",
                     "Versions/../catalog.json",
                     "/catalog.json",
                     "C:/catalog.json",
                     "C:\\catalog.json",
                     "//server/share/catalog.json",
                     "Versions//catalog.json",
                     "catalog.yaml"
                 })
        {
            var version = CreateVersion();
            version.CatalogFile = catalogFile;
            TestAssert.Throws<InvalidDataException>(version.Validate, "catalog file");
        }
    }

    internal static AssetBundleCatalog CreateCatalog() => new()
    {
        PackageName = "com.bengine.tests.hotupdate",
        Version = "1.2.3",
        Bundles =
        [
            new AssetBundleDescriptor
            {
                Name = "shared",
                FileName = $"{SharedBundleHash}.bassetbundle",
                Sha256 = SharedBundleHash,
                Size = 17
            },
            new AssetBundleDescriptor
            {
                Name = "main",
                FileName = $"{MainBundleHash}.bassetbundle",
                Sha256 = MainBundleHash,
                Size = 23,
                Dependencies = ["shared"]
            }
        ],
        Assets =
        [
            new AssetBundleAsset
            {
                Address = "Assets/Data/config.txt",
                Guid = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Bundle = "shared",
                Entry = "Assets/Data/config.txt",
                AssetType = "BEngine.TextAsset",
                Sha256 = ConfigAssetHash,
                Size = 6
            },
            new AssetBundleAsset
            {
                Address = "Assets/Scenes/Main.scene.yaml",
                Guid = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Bundle = "main",
                Entry = "Assets/Scenes/Main.scene.yaml",
                AssetType = "BEngine.Scene",
                Sha256 = SceneAssetHash,
                Size = 11
            }
        ]
    };

    internal static AssetBundleVersion CreateVersion() => new()
    {
        PackageName = "com.bengine.tests.hotupdate",
        Version = "1.2.3",
        CatalogFile = "catalog.json",
        CatalogSha256 = SharedBundleHash,
        CatalogSize = 128
    };
}
