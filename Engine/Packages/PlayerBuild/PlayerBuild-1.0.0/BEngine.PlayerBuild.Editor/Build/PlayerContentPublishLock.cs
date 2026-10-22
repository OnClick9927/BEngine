using BEngine.Platform;

namespace BEngine.Editor;

internal sealed class PlayerContentPublishLock : IDisposable
{
    private const string LockDirectoryName = "ContentPublishLocks";

    private readonly Stream _stream;

    private PlayerContentPublishLock(string path, Stream stream)
    {
        Path = path;
        _stream = stream;
    }

    internal string Path { get; }

    internal static PlayerContentPublishLock Acquire(
        string outputDirectory,
        string packageName,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        var output = pathService.GetFullPath(outputDirectory);
        fileSystem.CreateDirectory(output);
        var publicationPath = pathService.GetFullPath(pathService.Combine(output, packageName));
        if (platformService.IsWindows || platformService.IsMacOS)
            publicationPath = publicationPath.ToUpperInvariant();
        var lockName = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(publicationPath)))
            .ToLowerInvariant() + ".lock";
        var localData = platformService.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackLockDirectory = pathService.Combine(
            platformService.TemporaryDirectory, "BEngine", LockDirectoryName);
        var lockDirectory = string.IsNullOrWhiteSpace(localData)
            ? fallbackLockDirectory
            : pathService.Combine(localData, "BEngine", LockDirectoryName);
        try
        {
            fileSystem.CreateDirectory(lockDirectory);
        }
        catch (Exception exception) when (
            !lockDirectory.Equals(fallbackLockDirectory, StringComparison.OrdinalIgnoreCase) &&
            exception is IOException or UnauthorizedAccessException)
        {
            lockDirectory = fallbackLockDirectory;
            fileSystem.CreateDirectory(lockDirectory);
        }
        var lockPath = pathService.Combine(lockDirectory, lockName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = fileSystem.OpenFile(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
                return new PlayerContentPublishLock(lockPath, stream);
            }
            catch (IOException)
            {
                cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(25));
            }
        }
    }

    public void Dispose() => _stream.Dispose();
}
