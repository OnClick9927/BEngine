using BEngine.Editor.Documents;
using BEngine.Platform;
using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>
/// Owns the editor's exclusive project lock and transient files.
/// </summary>
public sealed class EditorInstanceContext : IDisposable
{
    private static EditorInstanceContext? _current;
    private readonly FileUtility _fileSystem;
    private readonly Stream _projectLock;
    private bool _disposed;

    public static EditorInstanceContext? current => Volatile.Read(ref _current);

    public string instanceId { get; }
    public string projectRootPath { get; }
    public string dataPath { get; }
    public string logsPath { get; }
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
        try
        {
            instanceId = $"{platformService.UtcNow:yyyyMMddHHmmssfff}-{platformService.ProcessId}-{Guid.NewGuid():N}";
            var instanceCacheRoot = pathService.Combine(this.projectRootPath, "Library", "EditorInstances");
            CleanupStaleCaches(instanceCacheRoot, fileSystem, pathService, platformService);
            dataPath = pathService.Combine(instanceCacheRoot, instanceId);
            logsPath = pathService.Combine(dataPath, "Logs");
            cachePath = pathService.Combine(dataPath, "Cache");
            tempPath = pathService.Combine(cachePath, "Temp");
            scriptAssembliesPath = pathService.Combine(cachePath, "ScriptAssemblies");
            packageCachePath = pathService.Combine(cachePath, "PackageCache");

            fileSystem.CreateDirectory(logsPath);
            fileSystem.CreateDirectory(tempPath);
            fileSystem.CreateDirectory(scriptAssembliesPath);
            fileSystem.CreateDirectory(packageCachePath);
            fileSystem.WriteAllText(pathService.Combine(dataPath, "Instance.yaml"),
                YamlUtility.Serialize(new EditorInstanceDocument
                {
                    InstanceId = instanceId,
                    ProcessId = platformService.ProcessId,
                    ProcessStartedUtc = platformService.ProcessStartTimeUtc,
                    ProjectPath = this.projectRootPath
                }));
        }
        catch
        {
            _projectLock.Dispose();
            throw;
        }
    }

    public static EditorInstanceContext Create(string projectRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        if (current is not null)
            throw new InvalidOperationException("An editor instance context is already active in this process.");
        var context = new EditorInstanceContext(
            projectRootPath,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformUtility>());
        if (Interlocked.CompareExchange(ref _current, context, null) is null) return context;
        context.Dispose();
        throw new InvalidOperationException("An editor instance context is already active in this process.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_fileSystem.DirectoryExists(cachePath))
                _fileSystem.DeleteDirectory(cachePath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            _projectLock.Dispose();
            Interlocked.CompareExchange(ref _current, null, this);
        }
        GC.SuppressFinalize(this);
    }

    private static void CleanupStaleCaches(
        string root,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService)
    {
        if (!fileSystem.DirectoryExists(root)) return;
        foreach (var directory in fileSystem.EnumerateDirectories(root))
        {
            var name = pathService.GetFileName(directory);
            var segments = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3 || !int.TryParse(segments[1], out var processId)) continue;
            if (platformService.IsProcessRunning(processId)) continue;
            // Keep instance metadata and logs; only the disposable cache is stale.
            // Older versions stored the cache directly under the instance directory.
            var cacheDirectory = fileSystem.FileExists(pathService.Combine(directory, "Instance.yaml"))
                ? pathService.Combine(directory, "Cache")
                : directory;
            if (!fileSystem.DirectoryExists(cacheDirectory)) continue;
            try { fileSystem.DeleteDirectory(cacheDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
