namespace BEngine.AssetBundles;

// Copy only immutable values for the provider's hot cache check. Public manager
// getters still return defensive copies of their mutable metadata documents.
internal readonly record struct AssetBundleResourceRevision(
    string? PackageName,
    string? Version,
    string? CatalogSha256,
    AssetBundleCatalog? CatalogIdentity = null)
{
    internal static AssetBundleResourceRevision FromVersion(AssetBundleVersion? version) =>
        version is null ? default : new(
            version.PackageName ?? string.Empty,
            version.Version ?? string.Empty,
            version.CatalogSha256 ?? string.Empty);

    internal static AssetBundleResourceRevision FromCatalog(AssetBundleCatalog? catalog) =>
        catalog is null ? default : new(
            catalog.PackageName ?? string.Empty,
            catalog.Version ?? string.Empty,
            null,
            catalog);
}
