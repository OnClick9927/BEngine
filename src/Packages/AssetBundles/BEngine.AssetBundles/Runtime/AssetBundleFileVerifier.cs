using BEngine.Platform;

namespace BEngine.AssetBundles;

internal static class AssetBundleFileVerifier
{
    internal static void Verify(
        FileUtility fileSystem,
        string path,
        long expectedSize,
        string expectedSha256,
        long maximumSize)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ValidateFile(fileSystem, path, expectedSize, maximumSize);
        using var stream = fileSystem.OpenFile(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var actual = AssetBundleCatalogSerializer.ComputeSha256(stream);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset bundle '{path}' failed its SHA256 verification.");
    }

    internal static async BValueTask VerifyAsync(
        FileUtility fileSystem,
        string path,
        long expectedSize,
        string expectedSha256,
        long maximumSize,
        BCancellationToken cancellationToken,
        Action<long>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ValidateFile(fileSystem, path, expectedSize, maximumSize);
        using var stream = fileSystem.OpenFile(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var actual = await AssetBundleCatalogSerializer.ComputeSha256WithProgressAsync(stream, cancellationToken, 1024 * 1024, progress);
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Asset bundle '{path}' failed its SHA256 verification.");
    }

    private static void ValidateFile(
        FileUtility fileSystem,
        string path,
        long expectedSize,
        long maximumSize)
    {
        if (!fileSystem.FileExists(path))
            throw new FileNotFoundException("Asset bundle file was not found.", path);
        var fileLength = fileSystem.GetFileLength(path);
        if (fileLength != expectedSize)
            throw new InvalidDataException($"Asset bundle '{path}' size does not match its catalog.");
        if (fileLength > maximumSize)
            throw new InvalidDataException($"Asset bundle '{path}' exceeds its runtime size limit.");
    }
}
