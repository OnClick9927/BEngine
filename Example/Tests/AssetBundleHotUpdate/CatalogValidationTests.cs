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
        FileSubAssetIdentityMustResolveToItsOwner();
        IndependentPayloadEntriesAreVersioned();
        BundleFilesMustBeContentAddressedLeafNames();
        AssetPayloadPathsMustBeCanonicalAndContained();
        VersionCatalogPathsMustBeCanonicalAndContained();
        VersionLabelsMustBePortableAndCanonical();
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

    private static void FileSubAssetIdentityMustResolveToItsOwner()
    {
        var valid = CreateCatalog();
        var owner = valid.Assets[0];
        valid.Assets.Add(CreateFileSubAsset(owner.Guid, owner.Bundle));
        valid.Validate();

        var orphan = CreateCatalog();
        var missingOwner = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        orphan.Assets.Add(CreateFileSubAsset(missingOwner, owner.Bundle));
        TestAssert.Throws<InvalidDataException>(orphan.Validate, "missing owner GUID");

        var wrongEntry = CreateCatalog();
        wrongEntry.SchemaVersion = 3;
        var childWithWrongEntry = CreateFileSubAsset(wrongEntry.Assets[0].Guid,
            wrongEntry.Assets[0].Bundle);
        childWithWrongEntry.Entry = "Assets/__BEngineSubAssets/wrong/2800000.png";
        wrongEntry.Assets.Add(childWithWrongEntry);
        TestAssert.Throws<InvalidDataException>(wrongEntry.Validate, "entry must be");

        var wrongBundle = CreateCatalog();
        wrongBundle.Assets.Add(CreateFileSubAsset(wrongBundle.Assets[0].Guid, "main"));
        TestAssert.Throws<InvalidDataException>(wrongBundle.Validate, "same bundle as its owner");
    }

    private static void IndependentPayloadEntriesAreVersioned()
    {
        var current = CreateCatalog();
        current.Assets[0].Entry = $"objects/{current.Assets[0].Sha256}.bin";
        current.Validate();

        var legacy = CreateCatalog();
        legacy.SchemaVersion = 3;
        legacy.Assets[0].Entry = $"objects/{legacy.Assets[0].Sha256}.bin";
        TestAssert.Throws<InvalidDataException>(legacy.Validate, "canonical payload path");

        var sharedPayload = CreateCatalog();
        sharedPayload.Assets[1].Bundle = sharedPayload.Assets[0].Bundle;
        sharedPayload.Assets[1].Entry = sharedPayload.Assets[0].Entry;
        sharedPayload.Assets[1].Sha256 = sharedPayload.Assets[0].Sha256;
        sharedPayload.Assets[1].Size = sharedPayload.Assets[0].Size;
        sharedPayload.Validate();

        sharedPayload.Assets[1].Size++;
        TestAssert.Throws<InvalidDataException>(sharedPayload.Validate, "Duplicate asset payload entry");
    }

    private static AssetBundleAsset CreateFileSubAsset(Guid ownerGuid, string bundle) => new()
    {
        Address = $"guid:{ownerGuid:N}#subasset=2800000",
        Guid = Guid.Parse("99999999-9999-9999-9999-999999999999"),
        OwnerGuid = ownerGuid,
        LocalIdentifier = 2800000,
        Bundle = bundle,
        Entry = $"Assets/__BEngineSubAssets/{ownerGuid:N}/2800000.png",
        AssetType = nameof(Texture),
        Sha256 = "9999999999999999999999999999999999999999999999999999999999999999",
        Size = 24
    };

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
        mismatchedEntry.Assets[0].Entry = "objects/config.bin";
        mismatchedEntry.Validate();

        mismatchedEntry.SchemaVersion = 3;
        mismatchedEntry.Assets[0].Entry = "Assets/Data/other.txt";
        TestAssert.Throws<InvalidDataException>(mismatchedEntry.Validate, "canonical Assets path");

        foreach (var entry in new[]
                 {
                     "../outside.bin",
                     "objects/../outside.bin",
                     "/outside.bin",
                     "C:/outside.bin",
                     "objects//payload.bin"
                 })
        {
            var catalog = CreateCatalog();
            catalog.Assets[0].Entry = entry;
            TestAssert.Throws<InvalidDataException>(catalog.Validate, "payload");
        }
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

    private static void VersionLabelsMustBePortableAndCanonical()
    {
        foreach (var label in new[] { "v1", "1.2.3", "managed-code-ab-1" })
        {
            var catalog = CreateCatalog();
            catalog.Version = label;
            catalog.Validate();
            var version = CreateVersion();
            version.Version = label;
            version.Validate();
        }

        foreach (var label in new[]
                 {
                     "V1", "V2", "Release-2", "con", "con.release", "prn", "aux", "nul", "com1",
                     "lpt9.cache", "v1.", ".v1", "v1/next", "v1\\next", "v 1", "v1 ", "*"
                 })
        {
            var catalog = CreateCatalog();
            catalog.Version = label;
            TestAssert.Throws<InvalidDataException>(catalog.Validate, nameof(catalog.Version));
            var version = CreateVersion();
            version.Version = label;
            TestAssert.Throws<InvalidDataException>(version.Validate, nameof(version.Version));
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
