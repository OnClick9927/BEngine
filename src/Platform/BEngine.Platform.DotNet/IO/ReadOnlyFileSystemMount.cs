using System.IO.Enumeration;

namespace BEngine.Platform;

/// <summary>Mounts indexed packaged files without creating physical copies.
/// The host owns the source; each open callback transfers a fresh stream to its caller.</summary>
public sealed class ReadOnlyFileSystemMount : IFileSystem
{
    private readonly IFileSystem _fallback;
    private readonly string _root;
    private readonly string _prefix;
    private readonly Dictionary<string, long> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "" };
    private readonly Func<string, Stream> _open;
    private readonly StringComparison _comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public ReadOnlyFileSystemMount(IFileSystem fallback, string root,
        IEnumerable<KeyValuePair<string, long>> files, Func<string, Stream> openRead)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _prefix = _root + Path.DirectorySeparatorChar;
        _open = openRead ?? throw new ArgumentNullException(nameof(openRead));
        foreach (var (name, length) in files)
        {
            var normalized = name.Replace('\\', '/');
            if (length < 0 || Path.IsPathRooted(normalized) || normalized.Contains(':') ||
                normalized.Split('/').Any(part => part is "" or "." or "..") || !_files.TryAdd(normalized, length))
                throw new InvalidDataException($"Invalid packaged file index entry '{name}'.");
            for (var slash = normalized.LastIndexOf('/'); slash >= 0;
                 slash = normalized.LastIndexOf('/', slash - 1))
                _directories.Add(normalized[..slash]);
        }
        if (_directories.Any(_files.ContainsKey))
            throw new InvalidDataException("A packaged path cannot be both a file and a directory.");
    }

    private bool Mounted(string path, out string relative)
    {
        var full = Path.GetFullPath(path);
        if (full.Equals(_root, _comparison)) { relative = ""; return true; }
        if (full.StartsWith(_prefix, _comparison))
        { relative = full[_prefix.Length..].Replace(Path.DirectorySeparatorChar, '/'); return true; }
        relative = "";
        return false;
    }

    private void Writable(string path, bool includeParents = false)
    {
        if (Mounted(path, out _) || includeParents &&
            _prefix.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) +
                               Path.DirectorySeparatorChar, _comparison))
            throw new UnauthorizedAccessException($"Packaged files are read-only: '{path}'.");
    }

    private string Full(string relative) => relative.Length == 0 ? _root :
        Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    public bool FileExists(string path) => Mounted(path, out var name)
        ? _files.ContainsKey(name) : _fallback.FileExists(path);
    public bool DirectoryExists(string path) => Mounted(path, out var name)
        ? _directories.Contains(name) : _fallback.DirectoryExists(path);
    public void CreateDirectory(string path) { Writable(path); _fallback.CreateDirectory(path); }

    private IEnumerable<string> Enumerate(string path, string pattern, SearchOption option, bool directories)
    {
        if (option is not (SearchOption.TopDirectoryOnly or SearchOption.AllDirectories))
            throw new ArgumentOutOfRangeException(nameof(option));
        if (!Mounted(path, out var relative))
        {
            var entries = directories ? _fallback.EnumerateDirectories(path, pattern, option)
                : _fallback.EnumerateFiles(path, pattern, option);
            foreach (var entry in entries)
                if (!Mounted(entry, out _)) yield return entry;
            var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var containsMount = _root.StartsWith(parent + Path.DirectorySeparatorChar, _comparison);
            if (containsMount && option == SearchOption.AllDirectories)
            {
                foreach (var name in directories ? _directories : _files.Keys.AsEnumerable())
                    if (FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(Full(name))))
                        yield return Full(name);
            }
            else if (directories && parent.Equals(Path.GetDirectoryName(_root), _comparison) &&
                     FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(_root)))
                yield return _root;
            yield break;
        }
        if (!_directories.Contains(relative)) throw new DirectoryNotFoundException(path);
        var prefix = relative.Length == 0 ? "" : relative + "/";
        foreach (var entry in directories ? _directories : _files.Keys.AsEnumerable())
        {
            if (entry.Length <= prefix.Length || !entry.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var child = entry[prefix.Length..];
            if (option == SearchOption.TopDirectoryOnly && child.Contains('/')) continue;
            if (FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(entry))) yield return Full(entry);
        }
    }

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) => Enumerate(path, searchPattern, searchOption, false);
    public IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) => Enumerate(path, searchPattern, searchOption, true);
    public IEnumerable<string> EnumerateFileSystemEntries(string path) =>
        EnumerateFiles(path).Concat(EnumerateDirectories(path));
    public void DeleteFile(string path) { Writable(path); _fallback.DeleteFile(path); }
    public void DeleteDirectory(string path, bool recursive = false)
    { Writable(path, true); _fallback.DeleteDirectory(path, recursive); }
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    { Writable(sourcePath); Writable(destinationPath); _fallback.MoveFile(sourcePath, destinationPath, overwrite); }
    public void MoveDirectory(string sourcePath, string destinationPath)
    { Writable(sourcePath, true); Writable(destinationPath, true); _fallback.MoveDirectory(sourcePath, destinationPath); }
    public void ReplaceFile(string sourcePath, string destinationPath, string? destinationBackupPath = null,
        bool ignoreMetadataErrors = false)
    {
        Writable(sourcePath); Writable(destinationPath);
        if (destinationBackupPath is not null) Writable(destinationBackupPath);
        _fallback.ReplaceFile(sourcePath, destinationPath, destinationBackupPath, ignoreMetadataErrors);
    }
    public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
    {
        Writable(destinationPath);
        if (!Mounted(sourcePath, out _)) { _fallback.CopyFile(sourcePath, destinationPath, overwrite); return; }
        using var source = OpenRead(sourcePath);
        using var destination = _fallback.OpenFile(destinationPath, overwrite ? FileMode.Create : FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
        source.CopyTo(destination);
    }
    public Stream OpenRead(string path)
    {
        if (!Mounted(path, out var name)) return _fallback.OpenRead(path);
        if (!_files.TryGetValue(name, out var size)) throw new FileNotFoundException("Packaged file not found.", path);
        var stream = _open(name);
        if (stream.CanRead && stream.CanSeek && stream.Position == 0 && stream.Length == size) return stream;
        stream.Dispose();
        throw new InvalidDataException($"The packaged file provider returned an invalid stream for '{path}'.");
    }
    public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, FileOptions options)
    {
        if (!Mounted(path, out _)) return _fallback.OpenFile(path, mode, access, share, bufferSize, options);
        if (mode != FileMode.Open || access != FileAccess.Read) Writable(path);
        return OpenRead(path);
    }
    public long GetFileLength(string path) => Mounted(path, out var name)
        ? _files.TryGetValue(name, out var length) ? length : throw new FileNotFoundException(path)
        : _fallback.GetFileLength(path);
    public DateTime GetLastWriteTimeUtc(string path) => Mounted(path, out _)
        ? DateTime.UnixEpoch : _fallback.GetLastWriteTimeUtc(path);
    public void SetLastWriteTimeUtc(string path, DateTime value) { Writable(path); _fallback.SetLastWriteTimeUtc(path, value); }
    public FileAttributes GetAttributes(string path) => !Mounted(path, out var name) ? _fallback.GetAttributes(path)
        : _directories.Contains(name) ? FileAttributes.Directory | FileAttributes.ReadOnly
        : _files.ContainsKey(name) ? FileAttributes.ReadOnly : throw new FileNotFoundException(path);
    public byte[] ReadAllBytes(string path)
    { using var stream = OpenRead(path); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    public string ReadAllText(string path) { using var stream = OpenRead(path); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    public void WriteAllBytes(string path, byte[] bytes) { Writable(path); _fallback.WriteAllBytes(path, bytes); }
    public void WriteAllText(string path, string contents) { Writable(path); _fallback.WriteAllText(path, contents); }
    public void Flush(Stream stream, bool flushToDisk = false) => _fallback.Flush(stream, flushToDisk);
}
