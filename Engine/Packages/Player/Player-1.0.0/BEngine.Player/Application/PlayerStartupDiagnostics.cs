using System.Text;
using System.Runtime;
using System.Threading;
using BEngine.Build;
using BEngine.Content;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Player;

internal sealed class PlayerStartupDiagnostics : IDisposable
{
    public const string LogPathEnvironmentVariable = "BENGINE_PLAYER_LOG_PATH";
    private const string LogDirectoryName = "logs";
    private const string LogFileName = "player.log";

    private static volatile PlayerStartupDiagnostics? _current;
    private static readonly object CurrentGate = new();
    private readonly BufferedPlayerLogWriter _writer;
    private readonly PlatformUtility _platform;
    private readonly bool _captureEngineLog;
    private readonly bool _captureProcessExit;
    private bool _disposed;

    private PlayerStartupDiagnostics(
        string logPath,
        bool captureEngineLog,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platform,
        ApplicationPathUtility applicationPaths)
    {
        LogPath = logPath;
        _captureEngineLog = captureEngineLog;
        _platform = platform;
        _captureProcessExit = !platform.IsBrowser;
        var host = $"process={platform.ProcessId}|base={applicationPaths.BaseDirectory}" +
            $"|serverGC={GCSettings.IsServerGC}|gcLatency={GCSettings.LatencyMode}" +
            $"|gcHeapCount={AppContext.GetData("System.GC.HeapCount") ?? "default"}" +
            $"|gcHeapCountEnvironment={platform.GetEnvironmentVariable("DOTNET_GCHeapCount") ?? platform.GetEnvironmentVariable("COMPlus_GCHeapCount") ?? "default"}";
        var directory = pathService.GetDirectoryName(logPath) ??
                        throw new InvalidDataException($"Player log path has no parent: '{logPath}'.");
        fileSystem.CreateDirectory(directory);
        var writer = new StreamWriter(fileSystem.OpenFile(
            logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
            16 * 1024, FileOptions.None), new UTF8Encoding(false), 16 * 1024)
        {
            AutoFlush = false
        };
        try
        {
            _writer = new BufferedPlayerLogWriter(writer, platform.WriteDiagnostic,
                useBackgroundThread: !platform.IsBrowser);
        }
        catch { writer.Dispose(); throw; }
        try
        {
            if (_captureEngineLog) Debug.MessageLogged += OnMessageLogged;
            _platform.UnhandledException += OnUnhandledException;
            if (_captureProcessExit) AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Write("HOST", host);
        }
        catch
        {
            if (_captureEngineLog) Debug.MessageLogged -= OnMessageLogged;
            _platform.UnhandledException -= OnUnhandledException;
            if (_captureProcessExit) AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            _writer.Dispose();
            throw;
        }
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
    {
        var services = CaptureServices();
        return StartAtPath(
            ResolveLogPath(runtimeResourcePath, manifest, persistentDataPath, services),
            captureEngineLog,
            services);
    }

    internal static PlayerStartupDiagnostics StartForPlayerRoot(
        string playerRoot,
        bool captureEngineLog = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerRoot);
        var services = CaptureServices();
        var explicitPath = services.Platform.GetEnvironmentVariable(LogPathEnvironmentVariable);
        var logPath = string.IsNullOrWhiteSpace(explicitPath)
            ? ResolveBootstrapFailureLogPath(playerRoot, services)
            : services.Paths.GetFullPath(explicitPath);
        return StartAtPath(logPath, captureEngineLog, services);
    }

    private static PlayerStartupDiagnostics StartAtPath(
        string path,
        bool captureEngineLog,
        DiagnosticServices services)
    {
        lock (CurrentGate)
        {
            // A new session must not open the same file while its predecessor
            // is still draining. Dispose never holds this gate while joining.
            while (_current is { _disposed: true }) Monitor.Wait(CurrentGate);
            if (_current is not null) return _current;
            try
            {
                return _current = new PlayerStartupDiagnostics(
                    path, captureEngineLog, services.FileSystem, services.Paths,
                    services.Platform, services.ApplicationPaths);
            }
            catch (Exception primaryFailure)
            {
                var fallback = services.Paths.Combine(
                    services.ApplicationPaths.TemporaryDirectory, "bengine", "playerlogs",
                    $"player-{services.Platform.ProcessId}.log");
                var diagnostics = new PlayerStartupDiagnostics(
                    fallback, captureEngineLog, services.FileSystem, services.Paths,
                    services.Platform, services.ApplicationPaths);
                diagnostics.Write("LOG_FALLBACK", $"requested={path}|error={primaryFailure.Message}");
                return _current = diagnostics;
            }
        }
    }

    internal static void Phase(string id, string? details = null)
    {
        _current?.Write("FLOW", string.IsNullOrWhiteSpace(details)
            ? id
            : $"{id}|{Sanitize(details)}");
    }

    internal static void Failure(string phase, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _current?.Write("FAIL", $"phase={phase}|{exception}", flush: true);
    }

    private void OnMessageLogged(LogEntry entry) =>
        Write("LOG", $"level={entry.Type}|{entry.Message}" +
                     (string.IsNullOrWhiteSpace(entry.StackTrace)
                         ? string.Empty
                         : "\n" + entry.StackTrace), flush: entry.Type == LogType.Error);

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        Write("UNHANDLED", args.ExceptionObject?.ToString() ?? "null", flush: true);

    private void OnProcessExit(object? sender, EventArgs args) => Dispose();

    private void Write(string category, string message, bool flush = false)
    {
        if (Volatile.Read(ref _disposed)) return;
        _writer.Write($"[{_platform.UtcNow:O}]|pid={_platform.ProcessId}|BENGINE_{category}|{message}", flush);
    }

    private static string ResolveLogPath(
        string runtimeResourcePath,
        PlayerBootstrapManifest? manifest,
        string? persistentDataPath,
        DiagnosticServices services)
    {
        var explicitPath = services.Platform.GetEnvironmentVariable(LogPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath)) return services.Paths.GetFullPath(explicitPath);

        if (manifest is not null)
        {
            var persistentRoot = string.IsNullOrWhiteSpace(persistentDataPath)
                ? PlayerPersistentDataPaths.ResolvePackaged(
                    runtimeResourcePath, manifest, services.FileSystem, services.Paths,
                    services.Platform, services.ApplicationPaths)
                : PlayerPersistentDataPaths.ResolveExplicitOverride(
                    persistentDataPath, services.Paths);
            return services.Paths.Combine(persistentRoot, LogDirectoryName, LogFileName);
        }

        var fullRuntimePath = services.Paths.GetFullPath(runtimeResourcePath);
        var dataDirectory = services.FileSystem.DirectoryExists(fullRuntimePath)
            ? services.Paths.GetDirectoryName(services.Paths.TrimEndingDirectorySeparator(fullRuntimePath))
            : services.Paths.GetDirectoryName(fullRuntimePath);
        if (!string.IsNullOrWhiteSpace(dataDirectory))
            return services.Paths.Combine(dataDirectory, LogDirectoryName, LogFileName);
        return services.Paths.Combine(
            services.ApplicationPaths.TemporaryDirectory, "bengine", "playerlogs", LogFileName);
    }

    private static string ResolveBootstrapFailureLogPath(
        string playerRoot,
        DiagnosticServices services)
    {
        var root = services.Paths.TrimEndingDirectorySeparator(
            services.Paths.GetFullPath(playerRoot));
        var persistentOverride = services.Platform.GetEnvironmentVariable(
            BEnginePlayer.PersistentDataPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(persistentOverride))
        {
            try
            {
                return services.Paths.Combine(
                    PlayerPersistentDataPaths.ResolveExplicitOverride(
                        persistentOverride, services.Paths),
                    LogDirectoryName, LogFileName);
            }
            catch
            {
                // Preserve bootstrap diagnostics even when the cache override itself is invalid.
            }
        }

        if (services.FileSystem.DirectoryExists(root))
        {
            var executableName = services.ApplicationPaths.ProcessPath is { Length: > 0 } processPath &&
                                 services.Paths.GetDirectoryName(services.Paths.GetFullPath(processPath))
                                     ?.Equals(root, StringComparison.OrdinalIgnoreCase) == true
                ? services.Paths.GetFileNameWithoutExtension(processPath)
                : null;
            string? dataDirectory = null;
            if (!string.IsNullOrWhiteSpace(executableName))
            {
                var directData = services.Paths.Combine(
                    root, executableName + PlayerPackagedResourceAddresses.DataDirectorySuffix);
                if (IsPhysicalDataDirectory(directData, services.FileSystem))
                    dataDirectory = directData;
            }

            if (dataDirectory is null)
            {
                var dataDirectories = services.FileSystem.EnumerateDirectories(
                        root, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => services.Paths.GetFileName(path).EndsWith(
                        PlayerPackagedResourceAddresses.DataDirectorySuffix,
                        StringComparison.OrdinalIgnoreCase))
                    .Where(path => IsPhysicalDataDirectory(path, services.FileSystem))
                    .Take(2)
                    .ToArray();
                if (dataDirectories.Length == 1) dataDirectory = dataDirectories[0];
            }

            if (dataDirectory is not null)
            {
                try
                {
                    var archivePath = services.Paths.Combine(
                        dataDirectory,
                        PlayerPackagedResourceAddresses.ResourcesDirectoryName,
                        PlayerPackagedResourceAddresses.PlayerArchiveFileName);
                    var runtimeRoot = services.FileSystem.FileExists(archivePath)
                        ? dataDirectory
                        : services.Paths.Combine(dataDirectory, "res");
                    var workspace = PlayerWorkspace.OpenRuntime(
                        runtimeRoot, services.FileSystem, services.Paths);
                    if (workspace.GetRuntimeMetadata()?.PlayerBootstrap is { } bootstrap)
                        return services.Paths.Combine(
                            PlayerPersistentDataPaths.ResolvePackaged(
                                runtimeRoot, bootstrap, services.FileSystem, services.Paths,
                                services.Platform, services.ApplicationPaths),
                            LogDirectoryName, LogFileName);
                }
                catch
                {
                    // A damaged bootstrap still needs a deterministic, writable diagnostic target.
                }
            }
        }
        return services.Paths.Combine(
            services.ApplicationPaths.TemporaryDirectory, "bengine", "playerlogs",
            $"player-{services.Platform.ProcessId}.log");
    }

    private static bool IsPhysicalDataDirectory(string path, FileUtility fileSystem) =>
        fileSystem.DirectoryExists(path) &&
        (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static DiagnosticServices CaptureServices() => new(
        RuntimeServiceAccess.FileSystem,
        RuntimeServiceAccess.PathService,
        RuntimeServiceAccess.PlatformService,
        RuntimeServiceAccess.ApplicationPaths);

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');

    public void Dispose()
    {
        lock (CurrentGate)
        {
            if (!_disposed)
            {
                Volatile.Write(ref _disposed, true);
                if (_captureEngineLog) Debug.MessageLogged -= OnMessageLogged;
                _platform.UnhandledException -= OnUnhandledException;
                if (_captureProcessExit) AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            }
        }
        try { _writer.Dispose(); }
        finally
        {
            lock (CurrentGate)
            {
                if (ReferenceEquals(_current, this)) _current = null;
                Monitor.PulseAll(CurrentGate);
            }
        }
    }

    private readonly record struct DiagnosticServices(
        FileUtility FileSystem,
        PathUtility Paths,
        PlatformUtility Platform,
        ApplicationPathUtility ApplicationPaths);
}
