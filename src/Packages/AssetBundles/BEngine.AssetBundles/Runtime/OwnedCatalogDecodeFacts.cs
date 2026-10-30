namespace BEngine.AssetBundles;
internal sealed class OwnedCatalogDecodeFacts(AssetBundleCatalog catalog, bool[] derivedEntries, bool canonical)
{
    private int _consumed;
    internal bool Canonical { get; } = canonical;
    internal bool[] Consume(AssetBundleCatalog ownedCatalog)
    {
        if (!ReferenceEquals(catalog, ownedCatalog) || Interlocked.Exchange(ref _consumed, 1) != 0)
            throw new ArgumentException("Decoded catalog facts require their original, unexposed catalog exactly once.", nameof(ownedCatalog));
        return derivedEntries;
    }
}
