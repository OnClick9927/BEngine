using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BEngine;
using BEngine.DependencyInjection;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection;

var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks.Add(name);
}
void Throws<T>(Action action, string name) where T : Exception
{
    try { action(); }
    catch (T) { Check(true, name); return; }
    throw new InvalidOperationException(name + " did not throw " + typeof(T).Name);
}

Check(!typeof(RuntimeServices).Assembly.GetCustomAttributes(typeof(InternalsVisibleToAttribute), false)
    .Cast<InternalsVisibleToAttribute>().Any(a => a.AssemblyName == typeof(Probe).Assembly.GetName().Name),
    "External consumer has no Core friend access");
Check(typeof(RuntimeServices).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
    System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name).Order().SequenceEqual(["GetRequiredService", "GetService"]),
    "Only borrowed service queries are public");
Check(typeof(RuntimeServices).Assembly.GetType("BEngine.RuntimeServiceAccess") is null, "Old private locator removed");

var platform = new Marker("platform");
using var platformServices = new Services(platform, new PlatformUtility(new ProbePlatform()), new RenderSettingsUtility(new ProbeRenderSettings()));
PlatformRuntime.Initialize(platformServices);
Check(ReferenceEquals(RuntimeServices.GetService<Marker>(), platform), "Platform fallback without an attachment");
Check(RuntimeServices.GetService<Missing>() is null, "Optional missing service returns null");
Throws<PlatformNotSupportedException>(() => RuntimeServices.GetRequiredService<Missing>(), "Required missing service reports failure");
Throws<ArgumentNullException>(() => PlatformServiceCollectionExtensions.AttachBEngineRuntimeServices(null!), "Null attachment rejected");

var outer = new Marker("outer");
var inner = new Marker("inner");
using var outerServices = new Services(outer);
using var innerServices = new Services(inner);
var first = outerServices.AttachBEngineRuntimeServices();
Check(ReferenceEquals(RuntimeServices.GetService<Marker>(), outer), "Attached runtime overrides platform");
var second = innerServices.AttachBEngineRuntimeServices();
Check(ReferenceEquals(RuntimeServices.GetRequiredService<Marker>(), inner), "Latest attachment wins");
first.Dispose(); first.Dispose();
Check(ReferenceEquals(RuntimeServices.GetService<Marker>(), inner), "Out of order and repeated detach preserve current scope");
second.Dispose(); second.Dispose();
Check(ReferenceEquals(RuntimeServices.GetService<Marker>(), platform), "Detaching all scopes restores platform");
Check(outer.Disposals == 0 && inner.Disposals == 0 && outerServices.Disposals == 0 && innerServices.Disposals == 0,
    "Detach never disposes borrowed services or providers");
using (outerServices.AttachBEngineRuntimeServices())
{
    using (innerServices.AttachBEngineRuntimeServices()) { }
    Check(ReferenceEquals(RuntimeServices.GetService<Marker>(), outer), "Nested detach restores previous attachment");
    using (new Services().AttachBEngineRuntimeServices())
        Check(ReferenceEquals(RuntimeServices.GetService<Marker>(), platform), "Missing latest attachment falls back to platform, not an older attachment");
}

var local = new Marker("scene");
var scopedCollection = new ServiceCollection();
scopedCollection.AddScoped(_ => local);
using var provider = scopedCollection.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
using var attachment = outerServices.AttachBEngineRuntimeServices();
using var scene = new Scene("service-scope", provider);
var probe = scene.CreateGameObject().AddComponent<Probe>();
var runtime = new SceneRuntime(scene);
Marker? awake = null, update = null, destroy = null, afterDispose = null;
Scene? unwindingScene = null;
probe.Awaking = () => awake = RuntimeServices.GetRequiredService<Marker>();
probe.Updating = () => update = RuntimeServices.GetRequiredService<Marker>();
probe.Destroying = () => destroy = RuntimeServices.GetRequiredService<Marker>();
runtime.Start();
Check(ReferenceEquals(awake, local), "Awake resolves the active scene scope");
Check(SceneRuntime.currentScene is null && ReferenceEquals(RuntimeServices.GetService<Marker>(), outer), "Start restores previous ambient scope");
runtime.Tick((Fix64)(1d / 60));
Check(ReferenceEquals(update, local), "Update resolves the active scene scope");
Check(SceneRuntime.currentScene is null && ReferenceEquals(RuntimeServices.GetService<Marker>(), outer), "Tick restores previous ambient scope");

long allocated = -1, ticks = 0;
bool same = true;
probe.Updating = () =>
{
    for (var i = 0; i < 10000; i++) _ = RuntimeServices.GetService<Marker>();
    var before = GC.GetAllocatedBytesForCurrentThread();
    var clock = Stopwatch.GetTimestamp();
    for (var i = 0; i < 100000; i++) same &= ReferenceEquals(RuntimeServices.GetService<Marker>(), local);
    ticks = Stopwatch.GetTimestamp() - clock;
    allocated = GC.GetAllocatedBytesForCurrentThread() - before;
};
runtime.Tick((Fix64)(1d / 60));
Check(same && allocated == 0, "100000 warmed scene lookups preserve identity and allocate zero bytes");
local.OnDispose = () => afterDispose = RuntimeServices.GetRequiredService<Marker>();
probe.Updating = () =>
{
    scene.Dispose();
    unwindingScene = SceneRuntime.currentScene;
    afterDispose = RuntimeServices.GetRequiredService<Marker>();
};
runtime.Tick((Fix64)(1d / 60));
Check(ReferenceEquals(destroy, local), "OnDestroy can still use its owning scene scope");
Check(local.Disposals == 1 && !scene.isCreated && !runtime.IsRunning, "Scene disposes its owned DI scope exactly once");
Check(ReferenceEquals(unwindingScene, scene) && ReferenceEquals(afterDispose, outer), "Destroyed current scene is skipped while its old callback unwinds");
Check(SceneRuntime.currentScene is null, "Disposed scene callback restores ambient context");
scene.Dispose();
Check(local.Disposals == 1 && outer.Disposals == 0, "Repeated scene disposal cannot dispose runtime attachment");

using (var emptyScene = new Scene("no-local-service"))
{
    Marker? fallback = null;
    var emptyProbe = emptyScene.CreateGameObject().AddComponent<Probe>();
    emptyProbe.Awaking = () => fallback = RuntimeServices.GetService<Marker>();
    var emptyRuntime = new SceneRuntime(emptyScene);
    emptyRuntime.Start();
    Check(ReferenceEquals(fallback, outer), "Active scene with no matching service falls back to attachment");
    emptyRuntime.Stop();
}

var componentsAllocated = ExtensionChecks.Run(Check);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    Passed = true, Checks = checks.Count, Tests = checks,
    SceneLookup = new { Iterations = 100000, AllocatedBytes = allocated, Milliseconds = ticks * 1000d / Stopwatch.Frequency },
    ComponentEnumerationAllocatedBytes = componentsAllocated,
    CoreBytes = new FileInfo(typeof(RuntimeServices).Assembly.Location).Length
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} runtime service boundary checks");

public sealed class Probe : MonoBehaviour
{
    public Action? Awaking, Updating, Destroying;
    public override void Awake() => Awaking?.Invoke();
    public override void Update() => Updating?.Invoke();
    public override void OnDestroy() => Destroying?.Invoke();
}
sealed class Missing;
sealed class Marker(string name) : IDisposable
{
    public readonly string Name = name;
    public int Disposals;
    public Action? OnDispose;
    public void Dispose() { Disposals++; OnDispose?.Invoke(); }
}
sealed class Services(params object[] values) : IServiceProvider, IDisposable
{
    public int Disposals;
    public object? GetService(Type serviceType) => values.FirstOrDefault(serviceType.IsInstanceOfType);
    public void Dispose() => Disposals++;
}
sealed class ProbePlatform : IPlatformService
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
sealed class ProbeRenderSettings : IRenderSettingsService
{
    public int targetFrameRate { get; set; } = 60;
    public Fix64 renderScale { get; set; } = Fix64.One;
    public int qualityLevel => 0;
    public string[] names => [];
    public int vSyncCount { get; set; }
    public int antiAliasing { get; set; }
    public bool realtimeReflectionProbes { get; set; }
    public Fix64 shadowDistance { get; set; }
    public void SetQualityLevel(int index, bool applyExpensiveChanges = true) { }
    public void IncreaseLevel(bool applyExpensiveChanges = false) { }
    public void DecreaseLevel(bool applyExpensiveChanges = false) { }
}
