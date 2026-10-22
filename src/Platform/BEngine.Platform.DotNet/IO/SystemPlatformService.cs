using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BEngine.Platform;

public sealed class SystemPlatformService : IPlatformService
{
    public static SystemPlatformService Shared { get; } = new();

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public bool IsBrowser => OperatingSystem.IsBrowser();
    public Architecture OperatingSystemArchitecture => RuntimeInformation.OSArchitecture;
    public Architecture ProcessArchitecture => RuntimeInformation.ProcessArchitecture;
    public string BaseDirectory => AppContext.BaseDirectory;
    public string CurrentDirectory
    {
        get => Environment.CurrentDirectory;
        set => Environment.CurrentDirectory = value;
    }
    public string TemporaryDirectory => Path.GetTempPath();
    public int ProcessId => Environment.ProcessId;
    public DateTime ProcessStartTimeUtc
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return process.StartTime.ToUniversalTime();
        }
    }
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long TickCount64 => Environment.TickCount64;
    public long MonotonicTimestamp => Stopwatch.GetTimestamp();
    public long TimestampFrequency => Stopwatch.Frequency;
    public TimeSpan GetElapsedTime(long startingTimestamp) => Stopwatch.GetElapsedTime(startingTimestamp);
    public string? ProcessPath => Environment.ProcessPath;
    public string FrameworkDescription => RuntimeInformation.FrameworkDescription;
    public string OperatingSystemDescription => RuntimeInformation.OSDescription;
    public string MachineName => Environment.MachineName;
    public int AvailableMemoryMegabytes => (int)Math.Clamp(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576L, 0, int.MaxValue);
    public CultureInfo CurrentUICulture => CultureInfo.CurrentUICulture;
    public bool IsNetworkAvailable => NetworkInterface.GetIsNetworkAvailable();
    public string NewLine => Environment.NewLine;
    public bool IsDebuggerAttached => Debugger.IsAttached;
    public event UnhandledExceptionEventHandler? UnhandledException
    {
        add => AppDomain.CurrentDomain.UnhandledException += value;
        remove => AppDomain.CurrentDomain.UnhandledException -= value;
    }
    public event AssemblyLoadEventHandler? AssemblyLoaded
    {
        add => AppDomain.CurrentDomain.AssemblyLoad += value;
        remove => AppDomain.CurrentDomain.AssemblyLoad -= value;
    }
    public IReadOnlyList<Assembly> GetLoadedAssemblies() => AppDomain.CurrentDomain.GetAssemblies();
    public string CaptureStackTrace(int skipFrames = 0) =>
        new StackTrace(Math.Max(0, skipFrames) + 1, true).ToString().TrimEnd();
    public void BreakIntoDebugger()
    {
        if (Debugger.IsAttached) Debugger.Break();
    }
    public void WriteDiagnostic(string message) => Trace.WriteLine(message);
    public IReadOnlyList<string> GetCommandLineArgs() => Environment.GetCommandLineArgs();
    public object? GetRuntimeData(string name) => AppContext.GetData(name);
    public void SetRuntimeData(string name, object? value) => AppContext.SetData(name, value);
    public string? GetEnvironmentVariable(string variable) =>
        Environment.GetEnvironmentVariable(variable);
    public string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target) =>
        Environment.GetEnvironmentVariable(variable, target);
    public string ExpandEnvironmentVariables(string value) =>
        Environment.ExpandEnvironmentVariables(value);
    public string GetFolderPath(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder);
    public bool IsProcessRunning(int processId)
    {
        if (processId <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
    public void OpenExternalUri(string uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}
