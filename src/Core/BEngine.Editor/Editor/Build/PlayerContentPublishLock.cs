namespace BEngine.Editor;

internal sealed class PlayerContentPublishLock : IDisposable
{
    private const string LockDirectoryName = "ContentPublishLocks";

    private readonly FileStream _stream;

    private PlayerContentPublishLock(string path, FileStream stream)
    {
        Path = path;
        _stream = stream;
    }

    internal string Path { get; }

    internal static PlayerContentPublishLock Acquire(
        string outputDirectory,
        string packageName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        var output = System.IO.Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var publicationPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(output, packageName));
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            publicationPath = publicationPath.ToUpperInvariant();
        var lockName = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(publicationPath)))
            .ToLowerInvariant() + ".lock";
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackLockDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "BEngine", LockDirectoryName);
        var lockDirectory = string.IsNullOrWhiteSpace(localData)
            ? fallbackLockDirectory
            : System.IO.Path.Combine(localData, "BEngine", LockDirectoryName);
        try
        {
            Directory.CreateDirectory(lockDirectory);
        }
        catch (Exception exception) when (
            !lockDirectory.Equals(fallbackLockDirectory, StringComparison.OrdinalIgnoreCase) &&
            exception is IOException or UnauthorizedAccessException)
        {
            lockDirectory = fallbackLockDirectory;
            Directory.CreateDirectory(lockDirectory);
        }
        var lockPath = System.IO.Path.Combine(lockDirectory, lockName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
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
