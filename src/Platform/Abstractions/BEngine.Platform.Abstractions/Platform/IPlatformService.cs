using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

public interface IPlatformService
{
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }
    bool IsBrowser { get; }
    Architecture OperatingSystemArchitecture { get; }
    Architecture ProcessArchitecture { get; }
    string BaseDirectory { get; }
    string CurrentDirectory { get; set; }
    string TemporaryDirectory { get; }
    int ProcessId { get; }
    DateTime ProcessStartTimeUtc { get; }
    DateTimeOffset UtcNow { get; }
    long TickCount64 { get; }
    long MonotonicTimestamp { get; }
    long TimestampFrequency { get; }
    TimeSpan GetElapsedTime(long startingTimestamp);
    string? ProcessPath { get; }
    string FrameworkDescription { get; }
    string OperatingSystemDescription { get; }
    string MachineName { get; }
    int AvailableMemoryMegabytes { get; }
    CultureInfo CurrentUICulture { get; }
    bool IsNetworkAvailable { get; }
    string NewLine { get; }
    bool IsDebuggerAttached { get; }
    event UnhandledExceptionEventHandler? UnhandledException;
    event AssemblyLoadEventHandler? AssemblyLoaded;
    IReadOnlyList<Assembly> GetLoadedAssemblies();
    string CaptureStackTrace(int skipFrames = 0);
    void BreakIntoDebugger();
    void WriteDiagnostic(string message);
    IReadOnlyList<string> GetCommandLineArgs();
    object? GetRuntimeData(string name);
    void SetRuntimeData(string name, object? value);
    string? GetEnvironmentVariable(string variable);
    string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target);
    string ExpandEnvironmentVariables(string value);
    string GetFolderPath(Environment.SpecialFolder folder);
    bool IsProcessRunning(int processId);
    void OpenExternalUri(string uri);
}
