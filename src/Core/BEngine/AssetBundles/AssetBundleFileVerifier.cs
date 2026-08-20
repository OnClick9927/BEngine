namespace BEngine.AssetBundles;

internal static class AssetBundleFileVerifier
{
    internal static async Task VerifyAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException("Asset bundle file was not found.", path);
        if (file.Length != expectedSize)
            throw new InvalidDataException($"Asset bundle '{path}' size does not match its catalog.");
        if (file.Length > maximumSize)
            throw new InvalidDataException($"Asset bundle '{path}' exceeds its runtime size limit.");
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await AssetBundleCatalogSerializer.ComputeSha256Async(stream, cancellationToken)
            .ConfigureAwait(false);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset bundle '{path}' failed its SHA256 verification.");
    }
}
