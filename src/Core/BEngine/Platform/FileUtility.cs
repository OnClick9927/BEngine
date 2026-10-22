namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IFileSystem through dependency injection.</summary>
public sealed class FileUtility
{
    private readonly IFileSystem _implementation;

    public FileUtility(IFileSystem implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static FileUtility Current => RuntimeServiceAccess.GetRequiredService<FileUtility>();

    public bool FileExists(string path) =>
        _implementation.FileExists(path);

    public bool DirectoryExists(string path) =>
        _implementation.DirectoryExists(path);

    public void CreateDirectory(string path) =>
        _implementation.CreateDirectory(path);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        _implementation.EnumerateFiles(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        _implementation.EnumerateDirectories(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        _implementation.EnumerateFileSystemEntries(path);

    public void DeleteFile(string path) =>
        _implementation.DeleteFile(path);

    public void DeleteDirectory(string path, bool recursive = false) =>
        _implementation.DeleteDirectory(path, recursive);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        _implementation.MoveFile(sourcePath, destinationPath, overwrite);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        _implementation.CopyFile(sourcePath, destinationPath, overwrite);

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        _implementation.MoveDirectory(sourcePath, destinationPath);

    public void ReplaceFile(string sourcePath, string destinationPath, string? destinationBackupPath = null, bool ignoreMetadataErrors = false) =>
        _implementation.ReplaceFile(sourcePath, destinationPath, destinationBackupPath, ignoreMetadataErrors);

    public Stream OpenRead(string path) =>
        _implementation.OpenRead(path);

    public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options) =>
        _implementation.OpenFile(path, mode, access, share, bufferSize, options);

    public long GetFileLength(string path) =>
        _implementation.GetFileLength(path);

    public DateTime GetLastWriteTimeUtc(string path) =>
        _implementation.GetLastWriteTimeUtc(path);

    public void SetLastWriteTimeUtc(string path, DateTime value) =>
        _implementation.SetLastWriteTimeUtc(path, value);

    public FileAttributes GetAttributes(string path) =>
        _implementation.GetAttributes(path);

    public byte[] ReadAllBytes(string path) =>
        _implementation.ReadAllBytes(path);

    public string ReadAllText(string path) =>
        _implementation.ReadAllText(path);

    public void WriteAllBytes(string path, byte[] bytes) =>
        _implementation.WriteAllBytes(path, bytes);

    public void WriteAllText(string path, string contents) =>
        _implementation.WriteAllText(path, contents);

    public void Flush(Stream stream, bool flushToDisk = false) =>
        _implementation.Flush(stream, flushToDisk);
}
