using BEngine.Build;
using BEngine.Content;
using BEngine.Hosting;
using BEngine.Platform;
using BEngine.Player.ProjectSystem;

namespace BEngine.Player;

// Deployment content selects paths; the engine owns capture and log lifetime.
internal static class PlayerStartupDiagnostics
{
    public const string LogPathEnvironmentVariable = PlayerStartupLog.LogPathEnvironmentVariable;
    private const string LogDirectoryName = "logs";
    private const string LogFileName = "player.log";

    internal static PlayerStartupLog Start(
        string runtimeResourcePath,
        bool captureEngineLog = true)
        => Start(runtimeResourcePath, null, null, captureEngineLog);

    internal static PlayerStartupLog Start(
        string runtimeResourcePath,
        HostDeploymentManifest? manifest,
        string? persistentDataPath,
        bool captureEngineLog = true)
    {
        var services = CaptureServices();
        return StartLog(
            ResolveLogPath(runtimeResourcePath, manifest, persistentDataPath, services),
            captureEngineLog,
            services);
    }

    internal static PlayerStartupLog StartForPlayerRoot(
        string playerRoot,
        bool captureEngineLog = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerRoot);
        var services = CaptureServices();
        var explicitPath = services.Platform.GetEnvironmentVariable(LogPathEnvironmentVariable);
        var logPath = string.IsNullOrWhiteSpace(explicitPath)
            ? ResolveBootstrapFailureLogPath(playerRoot, services)
            : services.Paths.GetFullPath(explicitPath);
        return StartLog(logPath, captureEngineLog, services);
    }

    private static PlayerStartupLog StartLog(string path, bool captureEngineLog,
        DiagnosticServices services) => PlayerStartupLog.Start(path, services.FileSystem,
            services.Paths, services.Platform, services.ApplicationPaths, captureEngineLog);

    internal static void Phase(string id, string? details = null) => PlayerStartupLog.Phase(id, details);
    internal static void Failure(string phase, Exception exception) => PlayerStartupLog.Failure(phase, exception);

    private static string ResolveLogPath(
        string runtimeResourcePath,
        HostDeploymentManifest? manifest,
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
                    root, executableName + HostPackagedResourceAddresses.DataDirectorySuffix);
                if (IsPhysicalDataDirectory(directData, services.FileSystem))
                    dataDirectory = directData;
            }

            if (dataDirectory is null)
            {
                var dataDirectories = services.FileSystem.EnumerateDirectories(
                        root, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => services.Paths.GetFileName(path).EndsWith(
                        HostPackagedResourceAddresses.DataDirectorySuffix,
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
                        HostPackagedResourceAddresses.ResourcesDirectoryName,
                        HostPackagedResourceAddresses.PlayerArchiveFileName);
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
        RuntimeServices.GetRequiredService<global::BEngine.Platform.FileUtility>(),
        RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>(),
        RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>(),
        RuntimeServices.GetRequiredService<global::BEngine.Platform.ApplicationPathUtility>());

    private readonly record struct DiagnosticServices(
        FileUtility FileSystem,
        PathUtility Paths,
        PlatformUtility Platform,
        ApplicationPathUtility ApplicationPaths);
}
