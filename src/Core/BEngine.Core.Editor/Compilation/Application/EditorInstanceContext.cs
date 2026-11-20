using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Serialization;
using BEngine.Tooling;
using System.Globalization;

namespace BEngine.Editor;

/// <summary>
/// Owns the editor's exclusive project lock and transient files.
/// </summary>
public sealed class EditorInstanceContext : IDisposable
{
    private static readonly object InstanceGate = new();
    private static EditorInstanceContext? _current;
    private readonly FileUtility _fileSystem;
    private readonly Stream _projectLock;
    private readonly string _logsPath;
    private readonly Dictionary<string, string?> _previousEnvironment;
    private bool _disposed;

    public static EditorInstanceContext? current => Volatile.Read(ref _current);

    public string instanceId { get; }
    public string projectRootPath { get; }
    public string dataPath { get; }
    public string logsPath => EnsureDirectory(_logsPath);
    public string cachePath { get; }
    public string tempPath { get; }
    public string scriptAssembliesPath { get; }
    public string packageCachePath { get; }

    private EditorInstanceContext(
        string projectRootPath,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        var candidate = pathService.GetFullPath(projectRootPath);
        this.projectRootPath = fileSystem.FileExists(candidate)
            ? pathService.GetDirectoryName(candidate) ??
              throw new InvalidDataException("Project path has no directory.")
            : candidate;
        var libraryPath = pathService.Combine(this.projectRootPath, "Library");
        fileSystem.CreateDirectory(libraryPath);
        var lockPath = pathService.Combine(libraryPath, ".bengine-editor.lock");
        try
        {
            _projectLock = fileSystem.OpenFile(lockPath, FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                $"Project '{this.projectRootPath}' is already open in another BEngine editor.", exception);
        }
        _previousEnvironment = new[] { "TEMP", "TMP", "TMPDIR", ToolEnvironment.ProjectDirectoryVariable }
            .ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);
        try
        {
            instanceId = $"{platformService.UtcNow:yyyyMMddHHmmssfff}-{platformService.ProcessId}-{Guid.NewGuid():N}";
            var instanceCacheRoot = pathService.Combine(this.projectRootPath, "Library", "EditorInstances");
            var instanceTempRoot = pathService.Combine(this.projectRootPath, "Temp", "EditorInstances");
            CleanupStaleTemporaryDirectories(instanceTempRoot, instanceCacheRoot, fileSystem, pathService, platformService);
            CleanupStaleCaches(instanceCacheRoot, fileSystem, pathService, platformService);
            dataPath = pathService.Combine(instanceCacheRoot, instanceId);
            _logsPath = pathService.Combine(dataPath, "Logs");
            cachePath = pathService.Combine(dataPath, "Cache");
            tempPath = pathService.Combine(instanceTempRoot, instanceId);
            scriptAssembliesPath = pathService.Combine(cachePath, "ScriptAssemblies");
            packageCachePath = pathService.Combine(cachePath, "PackageCache");

            fileSystem.CreateDirectory(dataPath);
            fileSystem.WriteAllText(pathService.Combine(dataPath, "Instance.yaml"),
                YamlUtility.Serialize(new EditorInstanceDocument
                {
                    InstanceId = instanceId,
                    ProcessId = platformService.ProcessId,
                    ProcessStartedUtc = platformService.ProcessStartTimeUtc,
                    ProjectPath = this.projectRootPath
                }));
            ToolEnvironment.ConfigureProject(this.projectRootPath);
        }
        catch
        {
            RestoreEnvironment();
            _projectLock.Dispose();
            throw;
        }
    }

    public static EditorInstanceContext Create(string projectRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        lock (InstanceGate)
        {
            if (current is not null)
                throw new InvalidOperationException("An editor instance context is already active in this process.");
            var context = new EditorInstanceContext(
                projectRootPath,
                EditorServiceAccess.GetRequiredService<FileUtility>(),
                EditorServiceAccess.GetRequiredService<PathUtility>(),
                EditorServiceAccess.GetRequiredService<PlatformUtility>());
            Volatile.Write(ref _current, context);
            return context;
        }
    }

    public void Dispose()
    {
        lock (InstanceGate)
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                DeleteDisposableDirectory(tempPath, _fileSystem);
                DeleteDisposableDirectory(cachePath, _fileSystem);
            }
            finally
            {
                RestoreEnvironment();
                _projectLock.Dispose();
                Interlocked.CompareExchange(ref _current, null, this);
            }
        }
        GC.SuppressFinalize(this);
    }

    private static void CleanupStaleCaches(
        string root,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        if (!fileSystem.DirectoryExists(root) ||
            (fileSystem.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        foreach (var directory in fileSystem.EnumerateDirectories(root))
        {
            if (!TryGetInstanceProcessId(pathService.GetFileName(directory), out var processId)) continue;
            if (platformService.IsProcessRunning(processId)) continue;
            if ((fileSystem.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            // Keep instance metadata and logs; only the disposable cache is stale.
            // Older versions stored the cache directly under the instance directory.
            var cacheDirectory = fileSystem.FileExists(pathService.Combine(directory, "Instance.yaml"))
                ? pathService.Combine(directory, "Cache")
                : directory;
            DeleteDisposableDirectory(cacheDirectory, fileSystem);
        }
    }

    private static void CleanupStaleTemporaryDirectories(
        string root,
        string instanceCacheRoot,
        FileUtility fileSystem,
        PathUtility paths,
        PlatformUtility platform)
    {
        if (!fileSystem.DirectoryExists(root) ||
            (fileSystem.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        foreach (var directory in fileSystem.EnumerateDirectories(root))
        {
            var instance = paths.GetFileName(directory);
            if (!TryGetInstanceProcessId(instance, out var processId) || platform.IsProcessRunning(processId))
                continue;
            // Match the retained instance record before removing anything from project Temp.
            // Unrelated user directories, including similarly named ones, belong to their owner.
            var metadataPath = paths.Combine(instanceCacheRoot, instance, "Instance.yaml");
            if (!fileSystem.FileExists(metadataPath)) continue;
            try
            {
                var metadata = YamlUtility.Deserialize<EditorInstanceDocument>(fileSystem.ReadAllText(metadataPath));
                if (metadata is null || metadata.Format != "BEngine.EditorInstance" ||
                    metadata.InstanceId != instance || metadata.ProcessId != processId) continue;
                DeleteDisposableDirectory(directory, fileSystem);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                // A retained log or an unreadable record must not block opening the project.
            }
        }
    }

    private static bool TryGetInstanceProcessId(string name, out int processId)
    {
        processId = 0;
        var segments = name.Split('-');
        return segments.Length == 3 &&
               DateTime.TryParseExact(segments[0], "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out _) &&
               int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
               processId > 0 && Guid.TryParseExact(segments[2], "N", out _);
    }

    private static void DeleteDisposableDirectory(string directory, FileUtility fileSystem)
    {
        try
        {
            if (fileSystem.DirectoryExists(directory) &&
                (fileSystem.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                fileSystem.DeleteDirectory(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string EnsureDirectory(string path)
    {
        _fileSystem.CreateDirectory(path);
        return path;
    }

    private void RestoreEnvironment()
    {
        foreach (var (name, value) in _previousEnvironment)
            Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }
}
