using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using BEngine;
using BEngine.Platform;
using BEngine.Player;

public static class LoggingProbe
{
    public static List<string> Run(string root, FileUtility files, PathUtility paths,
        PlatformUtility host, IPlatformService implementation)
    {
        var checks = new List<string>();
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        void Reject<T>(Action action, string name) where T : Exception
        {
            try { action(); }
            catch (T) { Check(true, name); return; }
            throw new InvalidOperationException(name);
        }
        static string Read(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        Directory.CreateDirectory(root);
        var observed = new ObservedPlatform(implementation);
        var platform = new PlatformUtility(observed);
        var appPaths = new ApplicationPathUtility(new ProbePaths(root, Path.Combine(root, "temporary")));
        var path = Path.Combine(root, "first.log");
        Check(typeof(PlayerStartupLog).Assembly == typeof(PlayerHost).Assembly,
            "Startup capture belongs to Core Player");
        Reject<ArgumentException>(() => PlayerStartupLog.Start("", files, paths, platform, appPaths),
            "Empty explicit log path is rejected before capture");
        Reject<ArgumentNullException>(() => PlayerStartupLog.Start(path, null!, paths, platform, appPaths),
            "Explicit host capabilities are validated");
        Reject<ArgumentNullException>(() => PlayerStartupLog.Failure("invalid", null!),
            "Null failure is rejected even without an active session");
        PlayerStartupLog.Phase("inactive");
        using (var log = PlayerStartupLog.Start(path, files, paths, platform, appPaths))
        {
            Check(log.LogPath == path && observed.Subscriptions == 1, "Session owns its path and exception subscription");
            var other = Path.Combine(root, "unused.log");
            Check(ReferenceEquals(log, PlayerStartupLog.Start(other, files, paths, platform, appPaths)) &&
                !File.Exists(other), "Starting an active session preserves its existing writer");
            PlayerStartupLog.Phase("probe", "detail\r\nvalue|tail");
            BEngine.Debug.Log("captured-info-marker");
            BEngine.Debug.LogError("captured-error-marker");
            Check(Read(path).Contains("captured-error-marker"), "Error records flush before returning");
            observed.Emit(new IOException("unhandled-marker"));
            Check(Read(path).Contains("BENGINE_UNHANDLED|System.IO.IOException: unhandled-marker"),
                "Platform exception notifications are captured without terminating the process");
            PlayerStartupLog.Failure("test", new InvalidDataException("failure-marker"));
            Check(Read(path).Contains("BENGINE_FAIL|phase=test|System.IO.InvalidDataException: failure-marker"),
                "Failure records retain the phase and exception and flush synchronously");
        }
        Check(observed.Subscriptions == 0, "Disposal detaches the platform exception handler");
        var first = Read(path);
        Check(first.Contains("BENGINE_HOST|process=") && first.Contains("captured-info-marker") &&
            first.Contains("BENGINE_FLOW|probe|detail  value/tail"),
            "Shutdown drains the host, information and sanitized flow records");
        BEngine.Debug.Log("after-dispose-marker");
        observed.Emit(new IOException("after-dispose-exception"));
        PlayerStartupLog.Phase("after-dispose-flow");
        Check(Read(path) == first, "Retired sessions receive no subsequent records");
        using (var appended = PlayerStartupLog.Start(path, files, paths, platform, appPaths, false))
        {
            BEngine.Debug.LogError("disabled-engine-capture-marker");
            PlayerStartupLog.Phase("appended");
            appended.Dispose();
            appended.Dispose();
        }
        var second = Read(path);
        Check(second.StartsWith(first, StringComparison.Ordinal) && second.Contains("BENGINE_FLOW|appended"),
            "A new session appends without replacing prior accepted records");
        Check(!second.Contains("disabled-engine-capture-marker") && observed.Subscriptions == 0,
            "Disabled engine capture and repeated disposal preserve hook ownership");

        var blocked = Path.Combine(root, "blocked-primary");
        Directory.CreateDirectory(blocked);
        using (var fallback = PlayerStartupLog.Start(blocked, files, paths, platform, appPaths, false))
        {
            Check(fallback.LogPath == Path.Combine(root, "temporary", "bengine", "playerlogs", $"player-{host.ProcessId}.log"),
                "An unavailable primary path falls back to the host temporary directory");
            PlayerStartupLog.Failure("fallback", new IOException("fallback-flush-marker"));
            var text = Read(fallback.LogPath);
            Check(text.Contains("BENGINE_LOG_FALLBACK|requested=") && text.Contains("fallback-flush-marker"),
                "Fallback logging retains the requested path and failure records");
        }
        var blockedTemporary = Path.Combine(root, "blocked-temporary");
        File.WriteAllText(blockedTemporary, "not a directory");
        var unavailable = new ApplicationPathUtility(new ProbePaths(root, blockedTemporary));
        Reject<IOException>(() => PlayerStartupLog.Start(blocked, files, paths, platform, unavailable),
            "Failure of both primary and fallback remains visible");
        Check(observed.Subscriptions == 0, "Failed construction leaves no platform event subscription");
        using (var retry = PlayerStartupLog.Start(Path.Combine(root, "retry.log"), files, paths, platform, appPaths))
            PlayerStartupLog.Failure("retry", new IOException("retry-marker"));
        Check(Read(Path.Combine(root, "retry.log")).Contains("retry-marker"),
            "A failed attempt does not poison subsequent sessions");
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is
            "BEngine.AssetBundles" or "BEngine.Graphics" or "BEngine.Rendering2D" or "BEngine.Application"),
            "Independent logging does not load resource, scene-rendering or framework packages");
        return checks;
    }

    private sealed class ProbePaths(string root, string temporary) : IApplicationPaths
    {
        public string BaseDirectory => root;
        public string CurrentDirectory => root;
        public string TemporaryDirectory => temporary;
        public string? ProcessPath => null;
        public string GetPersistentDataDirectory(string company, string product) => Path.Combine(root, company, product);
    }

    private sealed class ObservedPlatform(IPlatformService inner) : IPlatformService
    {
        private UnhandledExceptionEventHandler? _unhandled;
        public int Subscriptions => _unhandled?.GetInvocationList().Length ?? 0;
        public void Emit(Exception exception) => _unhandled?.Invoke(this, new UnhandledExceptionEventArgs(exception, false));
        public event UnhandledExceptionEventHandler? UnhandledException { add => _unhandled += value; remove => _unhandled -= value; }
        public event AssemblyLoadEventHandler? AssemblyLoaded { add => inner.AssemblyLoaded += value; remove => inner.AssemblyLoaded -= value; }
        public bool IsWindows => inner.IsWindows;
        public bool IsLinux => inner.IsLinux;
        public bool IsMacOS => inner.IsMacOS;
        public bool IsBrowser => inner.IsBrowser;
        public Architecture OperatingSystemArchitecture => inner.OperatingSystemArchitecture;
        public Architecture ProcessArchitecture => inner.ProcessArchitecture;
        public string BaseDirectory => inner.BaseDirectory;
        public string CurrentDirectory { get => inner.CurrentDirectory; set => inner.CurrentDirectory = value; }
        public string TemporaryDirectory => inner.TemporaryDirectory;
        public int ProcessId => inner.ProcessId;
        public DateTime ProcessStartTimeUtc => inner.ProcessStartTimeUtc;
        public DateTimeOffset UtcNow => inner.UtcNow;
        public long TickCount64 => inner.TickCount64;
        public long MonotonicTimestamp => inner.MonotonicTimestamp;
        public long TimestampFrequency => inner.TimestampFrequency;
        public TimeSpan GetElapsedTime(long start) => inner.GetElapsedTime(start);
        public string? ProcessPath => inner.ProcessPath;
        public string FrameworkDescription => inner.FrameworkDescription;
        public string OperatingSystemDescription => inner.OperatingSystemDescription;
        public string MachineName => inner.MachineName;
        public int AvailableMemoryMegabytes => inner.AvailableMemoryMegabytes;
        public CultureInfo CurrentUICulture => inner.CurrentUICulture;
        public bool IsNetworkAvailable => inner.IsNetworkAvailable;
        public string NewLine => inner.NewLine;
        public bool IsDebuggerAttached => inner.IsDebuggerAttached;
        public IReadOnlyList<Assembly> GetLoadedAssemblies() => inner.GetLoadedAssemblies();
        public string CaptureStackTrace(int skip = 0) => inner.CaptureStackTrace(skip);
        public void BreakIntoDebugger() => inner.BreakIntoDebugger();
        public void WriteDiagnostic(string message) => inner.WriteDiagnostic(message);
        public IReadOnlyList<string> GetCommandLineArgs() => inner.GetCommandLineArgs();
        public object? GetRuntimeData(string name) => inner.GetRuntimeData(name);
        public void SetRuntimeData(string name, object? value) => inner.SetRuntimeData(name, value);
        public string? GetEnvironmentVariable(string variable) => inner.GetEnvironmentVariable(variable);
        public string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target) => inner.GetEnvironmentVariable(variable, target);
        public string ExpandEnvironmentVariables(string value) => inner.ExpandEnvironmentVariables(value);
        public string GetFolderPath(Environment.SpecialFolder folder) => inner.GetFolderPath(folder);
        public bool IsProcessRunning(int id) => inner.IsProcessRunning(id);
        public void OpenExternalUri(string uri) => inner.OpenExternalUri(uri);
    }
}
