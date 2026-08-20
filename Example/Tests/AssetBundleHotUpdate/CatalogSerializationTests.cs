using System.Text;
using BEngine.AssetBundles;

namespace BEngine.ExampleTests.AssetBundleHotUpdate;

internal static class CatalogSerializationTests
{
    private const string ExtraBundleHash =
        "5555555555555555555555555555555555555555555555555555555555555555";

    internal static void Run()
    {
        EquivalentCatalogsSerializeByteForByteIdentically();
        CatalogRoundTripsWithoutLosingCanonicalOrder();
        StrictJsonRejectsAmbiguousOrUnknownProperties();
        HashingIsStableAcrossBufferingModes();
    }

    private static void EquivalentCatalogsSerializeByteForByteIdentically()
    {
        var first = CreateCatalogWithExtraDependency();
        var second = CreateCatalogWithExtraDependency();
        second.Bundles.Reverse();
        second.Assets.Reverse();
        second.Bundles.Single(item => item.Name == "main").Dependencies.Reverse();
        foreach (var bundle in second.Bundles)
        {
            bundle.Sha256 = bundle.Sha256.ToUpperInvariant();
            bundle.FileName = bundle.FileName.ToUpperInvariant();
        }
        foreach (var asset in second.Assets) asset.Sha256 = asset.Sha256.ToUpperInvariant();

        var firstBytes = AssetBundleCatalogSerializer.SerializeCatalog(first);
        var secondBytes = AssetBundleCatalogSerializer.SerializeCatalog(second);
        TestAssert.That(firstBytes.AsSpan().SequenceEqual(secondBytes),
            "Equivalent catalogs did not serialize to identical canonical bytes.");
        TestAssert.That(firstBytes.AsSpan().SequenceEqual(AssetBundleCatalogSerializer.SerializeCatalog(first)),
            "Serializing the same catalog twice produced different bytes.");
    }

    private static void CatalogRoundTripsWithoutLosingCanonicalOrder()
    {
        var bytes = AssetBundleCatalogSerializer.SerializeCatalog(CreateCatalogWithExtraDependency());
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(bytes);

        TestAssert.That(catalog.Bundles.Select(item => item.Name).SequenceEqual(
                catalog.Bundles.Select(item => item.Name).OrderBy(item => item, StringComparer.Ordinal)),
            "Canonical catalog bundle order was not ordinal.");
        TestAssert.That(catalog.Assets.Select(item => item.Address).SequenceEqual(
                catalog.Assets.Select(item => item.Address).OrderBy(item => item, StringComparer.Ordinal)),
            "Canonical catalog asset order was not ordinal.");
        TestAssert.That(catalog.Bundles.Single(item => item.Name == "main").Dependencies.SequenceEqual(
                new[] { "extra", "shared" }),
            "Canonical dependency order was not ordinal.");
        TestAssert.That(bytes.AsSpan().SequenceEqual(AssetBundleCatalogSerializer.SerializeCatalog(catalog)),
            "A canonical catalog changed bytes after round-trip serialization.");
    }

    private static void StrictJsonRejectsAmbiguousOrUnknownProperties()
    {
        var json = Encoding.UTF8.GetString(
            AssetBundleCatalogSerializer.SerializeCatalog(CatalogValidationTests.CreateCatalog()));
        var duplicateProperty = json.Replace(
            "\"packageName\":",
            "\"packageName\":\"shadow\",\"packageName\":",
            StringComparison.Ordinal);
        TestAssert.Throws<InvalidDataException>(
            () => AssetBundleCatalogSerializer.DeserializeCatalog(duplicateProperty),
            "duplicate property");

        var unknownProperty = json.Insert(json.Length - 1, ",\"unexpected\":true");
        TestAssert.Throws<Exception>(
            () => AssetBundleCatalogSerializer.DeserializeCatalog(unknownProperty),
            "unexpected");
    }

    private static void HashingIsStableAcrossBufferingModes()
    {
        var bytes = Encoding.UTF8.GetBytes("abc");
        const string expected =
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        TestAssert.That(AssetBundleCatalogSerializer.ComputeSha256(bytes) == expected,
            "Span SHA-256 did not match the standard test vector.");
        using var stream = new MemoryStream(bytes, writable: false);
        TestAssert.That(AssetBundleCatalogSerializer.ComputeSha256(stream) == expected,
            "Stream SHA-256 did not match the standard test vector.");
    }

    private static AssetBundleCatalog CreateCatalogWithExtraDependency()
    {
        var catalog = CatalogValidationTests.CreateCatalog();
        catalog.Bundles.Add(new AssetBundleDescriptor
        {
            Name = "extra",
            FileName = $"{ExtraBundleHash}.bassetbundle",
            Sha256 = ExtraBundleHash,
            Size = 29
        });
        catalog.Bundles.Single(item => item.Name == "main").Dependencies = ["shared", "extra"];
        return catalog;
    }
}
