namespace BEngine.Platform;

public sealed class PhysicalFileSystem : IFileSystem
{
    public static PhysicalFileSystem Shared { get; } = new();

    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.EnumerateFiles(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.EnumerateDirectories(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        Directory.EnumerateFileSystemEntries(path);

    public void DeleteFile(string path) => File.Delete(path);
    public void DeleteDirectory(string path, bool recursive = false) =>
        Directory.Delete(path, recursive);

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);
    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    public void ReplaceFile(
        string sourcePath,
        string destinationPath,
        string? destinationBackupPath = null,
        bool ignoreMetadataErrors = false) =>
        File.Replace(sourcePath, destinationPath, destinationBackupPath, ignoreMetadataErrors);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public Stream OpenFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        int bufferSize,
        FileOptions options) =>
        new FileStream(path, mode, access, share, bufferSize, options);

    public long GetFileLength(string path) => new FileInfo(path).Length;
    public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);
    public void SetLastWriteTimeUtc(string path, DateTime value) => File.SetLastWriteTimeUtc(path, value);
    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
    public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

    public void Flush(Stream stream, bool flushToDisk = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (flushToDisk && stream is FileStream fileStream)
            fileStream.Flush(flushToDisk: true);
        else
            stream.Flush();
    }
}
