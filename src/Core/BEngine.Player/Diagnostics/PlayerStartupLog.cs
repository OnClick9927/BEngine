using BEngine.Platform;
using System.Runtime;
using System.Text;
using System.Threading;

namespace BEngine.Player;

/// <summary>Engine-owned startup log capture and lifetime, independent of deployment content.</summary>
public sealed class PlayerStartupLog : IPlayerStartupLog
{
    public const string LogPathEnvironmentVariable = "BENGINE_PLAYER_LOG_PATH";

    private static volatile PlayerStartupLog? _current;
    private static readonly object CurrentGate = new();
    private readonly IPlayerLogWriter _writer;
    private readonly PlatformUtility _platform;
    private readonly bool _captureEngineLog;
    private readonly bool _captureProcessExit;
    private bool _disposed;

    private PlayerStartupLog(
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
            _writer = PlayerHostServices.CreateBufferedLogWriter(writer, platform.WriteDiagnostic,
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

    public string LogPath { get; }

    /// <summary>Starts the process-wide startup log at a caller-selected location.</summary>
    /// <remarks>Content owns path selection. Core owns capture, ordered writing and shutdown.
    /// An already active log is reused. Dispose it before starting another session.</remarks>
    public static PlayerStartupLog Start(string path, bool captureEngineLog = true) =>
        Start(path, RuntimeServices.GetRequiredService<FileUtility>(),
            RuntimeServices.GetRequiredService<PathUtility>(),
            RuntimeServices.GetRequiredService<PlatformUtility>(),
            RuntimeServices.GetRequiredService<ApplicationPathUtility>(), captureEngineLog);

    /// <summary>Starts capture using explicit host capabilities, without a content package.</summary>
    public static PlayerStartupLog Start(string path, FileUtility fileSystem,
        PathUtility pathService, PlatformUtility platform,
        ApplicationPathUtility applicationPaths, bool captureEngineLog = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        return StartAtPath(pathService.GetFullPath(path), captureEngineLog,
            new DiagnosticServices(fileSystem, pathService, platform, applicationPaths));
    }

    private static PlayerStartupLog StartAtPath(
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
                return _current = new PlayerStartupLog(
                    path, captureEngineLog, services.FileSystem, services.Paths,
                    services.Platform, services.ApplicationPaths);
            }
            catch (Exception primaryFailure)
            {
                var fallback = services.Paths.Combine(
                    services.ApplicationPaths.TemporaryDirectory, "bengine", "playerlogs",
                    $"player-{services.Platform.ProcessId}.log");
                var diagnostics = new PlayerStartupLog(
                    fallback, captureEngineLog, services.FileSystem, services.Paths,
                    services.Platform, services.ApplicationPaths);
                diagnostics.Write("LOG_FALLBACK", $"requested={path}|error={primaryFailure.Message}");
                return _current = diagnostics;
            }
        }
    }

    public static void Phase(string id, string? details = null)
    {
        _current?.Write("FLOW", string.IsNullOrWhiteSpace(details)
            ? id
            : $"{id}|{Sanitize(details)}");
    }

    public static void Failure(string phase, Exception exception)
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
