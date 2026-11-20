using System.Diagnostics;
using BEngine.Platform;

sealed class HeadlessPlatform : IPlatformService
{
    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public bool IsBrowser => false;
    public System.Runtime.InteropServices.Architecture OperatingSystemArchitecture => System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
    public System.Runtime.InteropServices.Architecture ProcessArchitecture => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture;
    public string BaseDirectory => AppContext.BaseDirectory;
    public string CurrentDirectory { get => Environment.CurrentDirectory; set => Environment.CurrentDirectory = value; }
    public string TemporaryDirectory => AppContext.BaseDirectory;
    public int ProcessId => Environment.ProcessId;
    public DateTime ProcessStartTimeUtc => DateTime.UnixEpoch;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public long TickCount64 => Environment.TickCount64;
    public long MonotonicTimestamp => Stopwatch.GetTimestamp();
    public long TimestampFrequency => Stopwatch.Frequency;
    public TimeSpan GetElapsedTime(long startingTimestamp) => Stopwatch.GetElapsedTime(startingTimestamp);
    public string? ProcessPath => Environment.ProcessPath;
    public string FrameworkDescription => ".NET probe";
    public string OperatingSystemDescription => "headless probe";
    public string MachineName => "probe";
    public int AvailableMemoryMegabytes => 1024;
    public System.Globalization.CultureInfo CurrentUICulture => System.Globalization.CultureInfo.InvariantCulture;
    public bool IsNetworkAvailable => false;
    public string NewLine => Environment.NewLine;
    public bool IsDebuggerAttached => false;
    public event UnhandledExceptionEventHandler? UnhandledException
    { add => AppDomain.CurrentDomain.UnhandledException += value; remove => AppDomain.CurrentDomain.UnhandledException -= value; }
    public event AssemblyLoadEventHandler? AssemblyLoaded
    { add => AppDomain.CurrentDomain.AssemblyLoad += value; remove => AppDomain.CurrentDomain.AssemblyLoad -= value; }
    public IReadOnlyList<System.Reflection.Assembly> GetLoadedAssemblies() => AppDomain.CurrentDomain.GetAssemblies();
    public string CaptureStackTrace(int skipFrames = 0) => new StackTrace(skipFrames).ToString();
    public void BreakIntoDebugger() => throw new NotSupportedException();
    public void WriteDiagnostic(string message) => Console.Error.WriteLine(message);
    public IReadOnlyList<string> GetCommandLineArgs() => Environment.GetCommandLineArgs();
    public object? GetRuntimeData(string name) => AppContext.GetData(name);
    public void SetRuntimeData(string name, object? value) => AppContext.SetData(name, value);
    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);
    public string? GetEnvironmentVariable(string variable, EnvironmentVariableTarget target) => Environment.GetEnvironmentVariable(variable, target);
    public string ExpandEnvironmentVariables(string value) => Environment.ExpandEnvironmentVariables(value);
    public string GetFolderPath(Environment.SpecialFolder folder) => AppContext.BaseDirectory;
    public bool IsProcessRunning(int processId) => processId == Environment.ProcessId;
    public void OpenExternalUri(string uri) => throw new NotSupportedException();
}
