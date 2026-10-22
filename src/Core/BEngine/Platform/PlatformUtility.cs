using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

/// <summary>Application-facing API; the platform host supplies IPlatformService through dependency injection.</summary>
public sealed class PlatformUtility
{
    private readonly IPlatformService _implementation;

    public PlatformUtility(IPlatformService implementation) =>
        _implementation = implementation ?? throw new ArgumentNullException(nameof(implementation));

    public static PlatformUtility Current => RuntimeServiceAccess.GetRequiredService<PlatformUtility>();

    public bool IsWindows => _implementation.IsWindows;

    public bool IsLinux => _implementation.IsLinux;

    public bool IsMacOS => _implementation.IsMacOS;

    public bool IsBrowser => _implementation.IsBrowser;

    public Architecture OperatingSystemArchitecture => _implementation.OperatingSystemArchitecture;

    public Architecture ProcessArchitecture => _implementation.ProcessArchitecture;

    public string BaseDirectory => _implementation.BaseDirectory;

    public string CurrentDirectory { get => _implementation.CurrentDirectory; set => _implementation.CurrentDirectory = value; }

    public string TemporaryDirectory => _implementation.TemporaryDirectory;

    public int ProcessId => _implementation.ProcessId;

    public DateTime ProcessStartTimeUtc => _implementation.ProcessStartTimeUtc;

    public DateTimeOffset UtcNow => _implementation.UtcNow;

    public long TickCount64 => _implementation.TickCount64;

    public long MonotonicTimestamp => _implementation.MonotonicTimestamp;

    public long TimestampFrequency => _implementation.TimestampFrequency;

    public TimeSpan GetElapsedTime(long startingTimestamp) =>
        _implementation.GetElapsedTime(startingTimestamp);

    public string? ProcessPath => _implementation.ProcessPath;

    public string FrameworkDescription => _implementation.FrameworkDescription;

    public string OperatingSystemDescription => _implementation.OperatingSystemDescription;

    public string MachineName => _implementation.MachineName;

    public int AvailableMemoryMegabytes => _implementation.AvailableMemoryMegabytes;

    public CultureInfo CurrentUICulture => _implementation.CurrentUICulture;

    public bool IsNetworkAvailable => _implementation.IsNetworkAvailable;

    public string NewLine => _implementation.NewLine;

    public bool IsDebuggerAttached => _implementation.IsDebuggerAttached;

    public event UnhandledExceptionEventHandler? UnhandledException
    {
        add => _implementation.UnhandledException += value;
        remove => _implementation.UnhandledException -= value;
    }

    public event AssemblyLoadEventHandler? AssemblyLoaded
    {
        add => _implementation.AssemblyLoaded += value;
        remove => _implementation.AssemblyLoaded -= value;
    }

    public IReadOnlyList<Assembly> GetLoadedAssemblies() =>
        _implementation.GetLoadedAssemblies();

    public string CaptureStackTrace(int skipFrames = 0) =>
        _implementation.CaptureStackTrace(skipFrames);

    public void BreakIntoDebugger() =>
        _implementation.BreakIntoDebugger();

    public void WriteDiagnostic(string message) =>
        _implementation.WriteDiagnostic(message);

    public IReadOnlyList<string> GetCommandLineArgs() =>
        _implementation.GetCommandLineArgs();

    public object? GetRuntimeData(string name) =>
        _implementation.GetRuntimeData(name);

    public void SetRuntimeData(string name, object? value) =>
        _implementation.SetRuntimeData(name, value);

    public string? GetEnvironmentVariable(string variable) =>
        _implementation.GetEnvironmentVariable(variable);

    public string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target) =>
        _implementation.GetEnvironmentVariable(variable, target);

    public string ExpandEnvironmentVariables(string value) =>
        _implementation.ExpandEnvironmentVariables(value);

    public string GetFolderPath(Environment.SpecialFolder folder) =>
        _implementation.GetFolderPath(folder);

    public bool IsProcessRunning(int processId) =>
        _implementation.IsProcessRunning(processId);

    public void OpenExternalUri(string uri) =>
        _implementation.OpenExternalUri(uri);
}
