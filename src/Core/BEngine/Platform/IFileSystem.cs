namespace BEngine.Platform;

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);
    IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly);
    IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly);
    IEnumerable<string> EnumerateFileSystemEntries(string path);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive = false);
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);
    void CopyFile(string sourcePath, string destinationPath, bool overwrite = false);
    void MoveDirectory(string sourcePath, string destinationPath);
    void ReplaceFile(
        string sourcePath,
        string destinationPath,
        string? destinationBackupPath = null,
        bool ignoreMetadataErrors = false);
    Stream OpenRead(string path);
    Stream OpenFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare share,
        int bufferSize,
        FileOptions options);
    long GetFileLength(string path);
    DateTime GetLastWriteTimeUtc(string path);
    void SetLastWriteTimeUtc(string path, DateTime value);
    FileAttributes GetAttributes(string path);
    byte[] ReadAllBytes(string path);
    string ReadAllText(string path);
    void WriteAllBytes(string path, byte[] bytes);
    void WriteAllText(string path, string contents);
    void Flush(Stream stream, bool flushToDisk = false);
}
