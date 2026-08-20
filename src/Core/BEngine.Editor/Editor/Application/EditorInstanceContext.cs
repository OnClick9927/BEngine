using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.Serialization;

namespace BEngine.Editor;

/// <summary>
/// Owns all files that must not be shared by two editor processes opening the same project.
/// Project assets and settings remain shared; logs, temporary builds and loaded assembly shadows do not.
/// </summary>
public sealed class EditorInstanceContext : IDisposable
{
    private static EditorInstanceContext? _current;
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

    private EditorInstanceContext(string projectRootPath)
    {
        var candidate = Path.GetFullPath(projectRootPath);
        this.projectRootPath = File.Exists(candidate)
            ? Path.GetDirectoryName(candidate) ?? throw new InvalidDataException("Project path has no directory.")
            : candidate;
        instanceId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var projectKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(this.projectRootPath.ToUpperInvariant())))[..16].ToLowerInvariant();
        dataPath = Path.Combine(EditorDataPaths.rootPath, "Instances", projectKey, instanceId);
        logsPath = Path.Combine(dataPath, "Logs");
        var instanceCacheRoot = Path.Combine(this.projectRootPath, "Library", "EditorInstances");
        CleanupStaleCaches(instanceCacheRoot);
        cachePath = Path.Combine(instanceCacheRoot, instanceId);
        tempPath = Path.Combine(cachePath, "Temp");
        scriptAssembliesPath = Path.Combine(cachePath, "ScriptAssemblies");
        packageCachePath = Path.Combine(cachePath, "PackageCache");

        Directory.CreateDirectory(logsPath);
        Directory.CreateDirectory(tempPath);
        Directory.CreateDirectory(scriptAssembliesPath);
        Directory.CreateDirectory(packageCachePath);
        new EditorInstanceDocument
        {
            InstanceId = instanceId,
            ProcessId = Environment.ProcessId,
            ProcessStartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            ProjectPath = this.projectRootPath
        }.Save(Path.Combine(dataPath, "Instance.yaml"));
    }

    public static EditorInstanceContext Create(string projectRootPath) => Create(projectRootPath, makeCurrent: true);

    public static EditorInstanceContext Create(string projectRootPath, bool makeCurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        var context = new EditorInstanceContext(projectRootPath);
        if (!makeCurrent) return context;
        if (Interlocked.CompareExchange(ref _current, context, null) is null) return context;
        context.Dispose();
        throw new InvalidOperationException("An editor instance context is already active in this process.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.CompareExchange(ref _current, null, this);
        try
        {
            if (Directory.Exists(cachePath)) Directory.Delete(cachePath, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        GC.SuppressFinalize(this);
    }

    private static void CleanupStaleCaches(string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            var segments = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 3 || !int.TryParse(segments[1], out var processId)) continue;
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited) continue;
            }
            catch (ArgumentException) { }
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
