using System.Text;
using BEngine.Build;
using BEngine.Content;
using BEngine.ProjectSystem;

namespace BEngine.Player;

internal sealed class PlayerStartupDiagnostics : IDisposable
{
    public const string LogPathEnvironmentVariable = "BENGINE_PLAYER_LOG_PATH";
    private const string LogDirectoryName = "logs";
    private const string LogFileName = "player.log";

    private static readonly object StaticGate = new();
    private static PlayerStartupDiagnostics? _current;
    private readonly object _writeGate = new();
    private readonly StreamWriter _writer;
    private readonly bool _captureEngineLog;
    private int _disposed;

    private PlayerStartupDiagnostics(string logPath, bool captureEngineLog)
    {
        LogPath = logPath;
        _captureEngineLog = captureEngineLog;
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite, 16 * 1024, FileOptions.WriteThrough), new UTF8Encoding(false))
        {
            AutoFlush = true
        };
        if (_captureEngineLog) Debug.MessageLogged += OnMessageLogged;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        Write("HOST", $"process={Environment.ProcessId}|base={AppContext.BaseDirectory}");
    }

    internal string LogPath { get; }

    internal static PlayerStartupDiagnostics Start(
        string runtimeResourcePath,
        bool captureEngineLog = true)
        => Start(runtimeResourcePath, null, null, captureEngineLog);

    internal static PlayerStartupDiagnostics Start(
        string runtimeResourcePath,
        PlayerBootstrapManifest? manifest,
        string? persistentDataPath,
        bool captureEngineLog = true)
        => StartAtPath(
            ResolveLogPath(runtimeResourcePath, manifest, persistentDataPath),
            captureEngineLog);

    internal static PlayerStartupDiagnostics StartForPlayerRoot(
        string playerRoot,
        bool captureEngineLog = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerRoot);
        var explicitPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
        var logPath = string.IsNullOrWhiteSpace(explicitPath)
            ? ResolveBootstrapFailureLogPath(playerRoot)
            : Path.GetFullPath(explicitPath);
        return StartAtPath(logPath, captureEngineLog);
    }

    private static PlayerStartupDiagnostics StartAtPath(string path, bool captureEngineLog)
    {
        lock (StaticGate)
        {
            if (_current is not null) return _current;
            try { return _current = new PlayerStartupDiagnostics(path, captureEngineLog); }
            catch (Exception primaryFailure)
            {
                var fallback = Path.Combine(Path.GetTempPath(), "bengine", "playerlogs",
                    $"player-{Environment.ProcessId}.log");
                var diagnostics = new PlayerStartupDiagnostics(fallback, captureEngineLog);
                diagnostics.Write("LOG_FALLBACK", $"requested={path}|error={primaryFailure.Message}");
                return _current = diagnostics;
            }
        }
    }

    internal static void Phase(string id, string? details = null)
    {
        lock (StaticGate) _current?.Write("FLOW", string.IsNullOrWhiteSpace(details)
            ? id
            : $"{id}|{Sanitize(details)}");
    }

    internal static void Failure(string phase, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (StaticGate) _current?.Write("FAIL", $"phase={phase}|{exception}");
    }

    private void OnMessageLogged(LogEntry entry) =>
        Write("LOG", $"level={entry.Type}|{entry.Message}" +
                     (string.IsNullOrWhiteSpace(entry.StackTrace)
                         ? string.Empty
                         : Environment.NewLine + entry.StackTrace));

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        Write("UNHANDLED", args.ExceptionObject?.ToString() ?? "null");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) =>
        Write("UNOBSERVED_TASK", args.Exception.ToString());

    private void Write(string category, string message)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        lock (_writeGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _writer.WriteLine(
                $"[{DateTimeOffset.UtcNow:O}]|pid={Environment.ProcessId}|tid={Environment.CurrentManagedThreadId}|" +
                $"BENGINE_{category}|{message}");
        }
    }

    private static string ResolveLogPath(
        string runtimeResourcePath,
        PlayerBootstrapManifest? manifest,
        string? persistentDataPath)
    {
        var explicitPath = Environment.GetEnvironmentVariable(LogPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath)) return Path.GetFullPath(explicitPath);

        if (manifest is not null)
        {
            var persistentRoot = string.IsNullOrWhiteSpace(persistentDataPath)
                ? PlayerPersistentDataPaths.ResolvePackaged(runtimeResourcePath, manifest)
                : PlayerPersistentDataPaths.ResolveExplicitOverride(persistentDataPath);
            return Path.Combine(persistentRoot, LogDirectoryName, LogFileName);
        }

        var fullRuntimePath = Path.GetFullPath(runtimeResourcePath);
        var dataDirectory = Directory.Exists(fullRuntimePath)
            ? Directory.GetParent(fullRuntimePath)?.FullName
            : Path.GetDirectoryName(fullRuntimePath);
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            return Path.Combine(dataDirectory, LogDirectoryName, LogFileName);
        return Path.Combine(Path.GetTempPath(), "bengine", "playerlogs", LogFileName);
    }

    private static string ResolveBootstrapFailureLogPath(string playerRoot)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(playerRoot));
        var persistentOverride = Environment.GetEnvironmentVariable(
            BEnginePlayer.PersistentDataPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(persistentOverride))
        {
            try
            {
                return Path.Combine(
                    PlayerPersistentDataPaths.ResolveExplicitOverride(persistentOverride),
                    LogDirectoryName, LogFileName);
            }
            catch
            {
                // Preserve bootstrap diagnostics even when the cache override itself is invalid.
            }
        }

        if (Directory.Exists(root))
        {
            var executableName = Environment.ProcessPath is { Length: > 0 } processPath &&
                                 Path.GetDirectoryName(Path.GetFullPath(processPath))
                                     ?.Equals(root, StringComparison.OrdinalIgnoreCase) == true
                ? Path.GetFileNameWithoutExtension(processPath)
                : null;
            string? dataDirectory = null;
            if (!string.IsNullOrWhiteSpace(executableName))
            {
                var directData = Path.Combine(
                    root, executableName + PlayerPackagedResourceAddresses.DataDirectorySuffix);
                if (IsPhysicalDataDirectory(directData))
                    dataDirectory = directData;
            }

            if (dataDirectory is null)
            {
                var dataDirectories = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => Path.GetFileName(path).EndsWith(
                        PlayerPackagedResourceAddresses.DataDirectorySuffix,
                        StringComparison.OrdinalIgnoreCase))
                    .Where(IsPhysicalDataDirectory)
                    .Take(2)
                    .ToArray();
                if (dataDirectories.Length == 1) dataDirectory = dataDirectories[0];
            }

            if (dataDirectory is not null)
            {
                try
                {
                    var archivePath = Path.Combine(
                        dataDirectory,
                        PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                        PlayerPackagedResourceAddresses.PlayerArchiveFileName);
                    var runtimeRoot = File.Exists(archivePath)
                        ? dataDirectory
                        : Path.Combine(dataDirectory, "res");
                    var workspace = ProjectWorkspace.OpenRuntime(runtimeRoot);
                    if (workspace.RuntimeMetadata?.PlayerBootstrap is { } bootstrap)
                        return Path.Combine(
                            bootstrap.ResolveCacheDirectory(root), LogDirectoryName, LogFileName);
                }
                catch
                {
                    // A damaged bootstrap still needs a deterministic, writable diagnostic target.
                }
                return Path.Combine(root, PlayerBootstrapManifest.DefaultCacheDirectory,
                    LogDirectoryName, LogFileName);
            }
        }
        return Path.Combine(Path.GetTempPath(), "bengine", "playerlogs",
            $"player-{Environment.ProcessId}.log");
    }

    private static bool IsPhysicalDataDirectory(string path) =>
        Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_captureEngineLog) Debug.MessageLogged -= OnMessageLogged;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        lock (_writeGate) _writer.Dispose();
        lock (StaticGate)
            if (ReferenceEquals(_current, this)) _current = null;
    }
}
