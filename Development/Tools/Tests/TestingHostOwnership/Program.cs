using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using BEngine.Build;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.Testing;
using BEngine.Testing.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

if (args.FirstOrDefault() == "--process-output")
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine(args[1] + "|" + Environment.GetEnvironmentVariable("BENGINE_TESTING_HOST_VALUE"));
    Console.Error.WriteLine("stderr 中文");
    Environment.ExitCode = 7;
    return;
}
if (Environment.GetEnvironmentVariable("BENGINE_TESTING_HOST_CHILD") is { Length: > 0 } child)
{
    File.WriteAllText(child + ".pid", Environment.ProcessId.ToString());
    if (Environment.GetEnvironmentVariable("BENGINE_TESTING_HOST_DELAY") == "1") await Task.Delay(Timeout.InfiniteTimeSpan);
    var path = Environment.GetEnvironmentVariable("BENGINE_TEST_RUNNER_RESULT");
    if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, JsonSerializer.Serialize(Results.Pass()));
    return;
}

var output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
BEngine.Platform.DotNet.DotNetPlatformServices.Initialize();
DotNetEditorPlatform.Initialize();
var checks = new List<string>();
void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
var factory = BEngine.Editor.EditorServices.GetRequiredService<IEditorHttpListenerFactory>();
Check(factory.GetType().Assembly.GetName().Name == "BEngine.Platform.DotNet.Editor", "Native HTTP is provided by the editor platform");
Check(typeof(IEditorHttpListenerFactory).Assembly.GetName().Name == "BEngine.Core.Editor", "The listening contract belongs to platform contracts");
using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
using (var listener = factory.ListenLoopback())
{
    Check(listener.BaseAddress.Host == "127.0.0.1" && listener.BaseAddress.Port > 0, "The native listener binds only IPv4 loopback on an ephemeral port");
    var accept = listener.AcceptAsync(timeout.Token);
    var send = client.PostAsync(new Uri(listener.BaseAddress, "probe/%E4%B8%AD%E6%96%87"), new StringContent("request 中文", Encoding.UTF8), timeout.Token);
    using var context = await accept;
    Check(context.Method == "POST" && context.Url.AbsolutePath.Contains("probe/") && context.RequestContentLength > 0,
        "Native request method, URL and length cross the neutral interface");
    using var reader = new StreamReader(context.RequestBody, Encoding.UTF8, leaveOpen: true);
    Check(await reader.ReadToEndAsync(timeout.Token) == "request 中文", "The borrowed native request stream preserves UTF-8 bytes");
    context.StatusCode = 201; context.ContentType = "text/plain; charset=utf-8";
    context.SetResponseHeader("X-Host-Probe", "preserved");
    var bytes = Encoding.UTF8.GetBytes("response 中文"); context.ResponseContentLength = bytes.Length;
    await context.ResponseBody.WriteAsync(bytes, timeout.Token); context.Dispose(); context.Dispose();
    using var response = await send;
    Check(response.StatusCode == HttpStatusCode.Created && response.Headers.GetValues("X-Host-Probe").Single() == "preserved" &&
        await response.Content.ReadAsStringAsync(timeout.Token) == "response 中文", "Native response status, headers, length and body survive idempotent completion");
}
var cancelled = factory.ListenLoopback();
using (var cancellation = new CancellationTokenSource())
{
    var accept = cancelled.AcceptAsync(cancellation.Token); cancellation.Cancel();
    try { await accept; throw new Exception("Uncancelled accept"); } catch (OperationCanceledException) { }
    Check(accept.IsCanceled, "Cancelling accept observes and terminates the pending native operation");
    using var rebound = new HttpListener(); rebound.Prefixes.Add(cancelled.BaseAddress.AbsoluteUri); rebound.Start();
    Check(rebound.IsListening, "Cancelled HTTP accept releases its native address");
}
cancelled.Dispose();
var disposed = factory.ListenLoopback(); var pending = disposed.AcceptAsync(); disposed.Dispose();
try { await pending.WaitAsync(TimeSpan.FromSeconds(5)); throw new Exception("Disposed listener accepted"); }
catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException) { checks.Add("Disposing a listener unblocks an outstanding native accept"); }

var locator = BEngine.Editor.EditorServices.GetRequiredService<IEditorHostTools>();
var previousSdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
var sdk = Path.Combine(output, "SdkFixture"); Directory.CreateDirectory(Path.Combine(sdk, "platform-tools"));
var fakeAdb = Path.Combine(sdk, "platform-tools", "adb.exe"); File.WriteAllBytes(fakeAdb, []);
try
{
    Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", sdk);
    Check(locator.FindAndroidDebugBridge() == fakeAdb, "Host tool discovery respects the configured Android SDK without running it");
}
finally { Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", previousSdk); }

var process = new ProcessRunRequest(Environment.ProcessPath!);
process.Arguments.Add("--process-output"); process.Arguments.Add("argument 中文 \"quoted\"");
process.EnvironmentVariables["BENGINE_TESTING_HOST_VALUE"] = "env 中文";
var exited = await ProcessUtility.Current.RunAsync(process, timeout.Token);
Check(exited.ExitCode == 7 && exited.StandardOutput.Contains("argument 中文 \"quoted\"|env 中文") && exited.StandardError.Contains("stderr 中文"),
    "The shared process runner preserves exit code, arguments, environment and both output streams");

var tracked = new TrackingFactory(factory); var runner = new TrackingRunner(BEngine.Editor.EditorServices.GetRequiredService<IProcessRunner>());
var tools = new TrackingTools(locator);
RuntimeServiceContributions.Register(Override);
void Override(IServiceCollection services)
{
    services.Replace(ServiceDescriptor.Singleton<IEditorHttpListenerFactory>(tracked));
    services.Replace(ServiceDescriptor.Singleton<IProcessRunner>(runner));
    services.Replace(ServiceDescriptor.Singleton<IEditorHostTools>(tools));
}
try
{
    var web = Path.Combine(output, "Web"); Directory.CreateDirectory(web);
    File.WriteAllText(Path.Combine(web, "index.html"), """
        <!doctype html><script>
        const value = new URLSearchParams(location.search).get('bengineEnvironment');
        const data = JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(value.replace(/-/g,'+').replace(/_/g,'/')),x=>x.charCodeAt(0))));
        const now = new Date().toISOString();
        fetch(data.environment.BENGINE_TEST_RUNNER_RESULT_URL,{method:'POST',body:JSON.stringify({StartedAt:now,FinishedAt:now,Tests:[],Coverage:null})});
        </script>
        """);
    var target = new BuildTargetDescriptor("web", BuildTargetPlatform.Web, BuildArchitecture.Wasm32, "browser-wasm", default, [], true);
    var build = new PlayerTestBuild(target, web, web);
    var request = new PlayerTestLaunchRequest(build, "http://127.0.0.1:12345/", Path.Combine(output, "WebResult.json"), false, []) { ProjectPath = output };
    var previousBrowser = Environment.GetEnvironmentVariable("BENGINE_TEST_BROWSER");
    try
    {
        Environment.SetEnvironmentVariable("BENGINE_TEST_BROWSER", null);
        await WebPlayerTestLauncher.Run(request, timeout.Token);
        Check(runner.Calls == 1 && tools.Browsers == 1, "Actual Edge launch resolves its browser and starts its process through replaceable host services");
        Check(tracked.Listeners == 0 && tracked.Contexts == 0, "Successful Edge reporting retires all native listener and response leases");
        Check(File.Exists(request.ResultPath), "The Testing-owned report is persisted before the host is retired");
        Environment.SetEnvironmentVariable("BENGINE_TEST_BROWSER", Path.Combine(output, "missing-browser.exe"));
        try { await WebPlayerTestLauncher.Run(request, timeout.Token); throw new Exception("Missing browser started"); }
        catch (System.ComponentModel.Win32Exception) { }
        Check(tracked.Listeners == 0 && tracked.Contexts == 0, "Failed process creation still retires the test HTTP host");
        var profiles = Path.Combine(output, "Temp", "TestRunner", "BrowserProfiles");
        Check(!Directory.EnumerateDirectories(profiles).Any(), "Success and launch failure both remove only their owned browser profile");
        Environment.SetEnvironmentVariable("BENGINE_TEST_BROWSER", null);
        File.WriteAllText(Path.Combine(web, "index.html"), "<!doctype html><title>No report</title>");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await WebPlayerTestLauncher.Run(request, cancel.Token); throw new Exception("Browser did not cancel"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Check(tracked.Listeners == 0 && tracked.Contexts == 0 && !Directory.EnumerateDirectories(profiles).Any(),
            "Cancellation joins the owned browser, retires native responses and removes its profile");
    }
    finally { Environment.SetEnvironmentVariable("BENGINE_TEST_BROWSER", previousBrowser); }

    var childKey = "BENGINE_TESTING_HOST_CHILD"; var delayKey = "BENGINE_TESTING_HOST_DELAY";
    var previousChild = Environment.GetEnvironmentVariable(childKey); var previousDelay = Environment.GetEnvironmentVariable(delayKey);
    var desktop = new PlayerTestBuild(new("windows", BuildTargetPlatform.Windows, BuildArchitecture.X64, "win-x64", default, [], true), output, web)
        { ExecutablePath = Environment.ProcessPath! };
    try
    {
        var childPath = Path.Combine(output, "Desktop"); Environment.SetEnvironmentVariable(childKey, childPath);
        Environment.SetEnvironmentVariable(delayKey, null);
        var run = await PlayerTestSession.RunProjectAsync(output, false, buildProvider: new ExistingBuild(desktop), cancellationToken: timeout.Token);
        Check(run.Succeeded && File.Exists(childPath + ".pid") && tracked.Listeners == 0, "Desktop Player sessions use the host runner and release their content listener");
        Environment.SetEnvironmentVariable(delayKey, "1");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await PlayerTestSession.RunProjectAsync(output, false, buildProvider: new ExistingBuild(desktop), cancellationToken: cancel.Token); throw new Exception("Desktop did not cancel"); }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        var pid = int.Parse(File.ReadAllText(childPath + ".pid"));
        bool alive; try { using var owned = Process.GetProcessById(pid); alive = !owned.HasExited; } catch (ArgumentException) { alive = false; }
        Check(!alive && tracked.Listeners == 0 && tracked.Contexts == 0, "Desktop cancellation joins only its owned child and retires all HTTP leases");
    }
    finally { Environment.SetEnvironmentVariable(childKey, previousChild); Environment.SetEnvironmentVariable(delayKey, previousDelay); }
}
finally { RuntimeServiceContributions.UnregisterAssembly(typeof(Results).Assembly); }
Check(BEngine.Editor.EditorServices.GetRequiredService<IEditorHttpListenerFactory>().GetType() == factory.GetType(),
    "Retiring the test contribution restores the native platform service");
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Count = checks.Count, Checks = checks,
    InjectedListenerLeases = tracked.Listeners, InjectedResponseLeases = tracked.Contexts, RunnerCalls = runner.Calls }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("TESTING_HOST_OWNERSHIP_OK|" + checks.Count);

static class Results
{
    public static TestRunResult Pass() => new(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], null);
}
sealed class ExistingBuild(PlayerTestBuild build) : IPlayerTestBuildProvider
{
    public Task<PlayerTestBuild> BuildAsync(PlayerTestBuildRequest request, IProgress<PlayerTestBuildProgress>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(build);
}
sealed class TrackingTools(IEditorHostTools inner) : IEditorHostTools
{
    public int Browsers;
    public string? FindChromiumBrowser() { Browsers++; return inner.FindChromiumBrowser(); }
    public string? FindAndroidDebugBridge() => inner.FindAndroidDebugBridge();
}
sealed class TrackingRunner(IProcessRunner inner) : IProcessRunner
{
    public int Calls;
    public ProcessRunResult Run(ProcessRunRequest request, CancellationToken token = default) { Interlocked.Increment(ref Calls); return inner.Run(request, token); }
    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken token = default) { Interlocked.Increment(ref Calls); return inner.RunAsync(request, token); }
}
sealed class TrackingFactory(IEditorHttpListenerFactory inner) : IEditorHttpListenerFactory
{
    public int Listeners, Contexts;
    public IEditorHttpListener ListenLoopback() { var listener = inner.ListenLoopback(); Interlocked.Increment(ref Listeners); return new Listener(this, listener); }
    sealed class Listener(TrackingFactory owner, IEditorHttpListener inner) : IEditorHttpListener
    {
        int disposed;
        public Uri BaseAddress => inner.BaseAddress;
        public async Task<IEditorHttpContext> AcceptAsync(CancellationToken token = default)
        { var context = await inner.AcceptAsync(token); Interlocked.Increment(ref owner.Contexts); return new Context(owner, context); }
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { try { inner.Dispose(); } finally { Interlocked.Decrement(ref owner.Listeners); } } }
    }
    sealed class Context(TrackingFactory owner, IEditorHttpContext inner) : IEditorHttpContext
    {
        int disposed;
        public string Method => inner.Method; public Uri Url => inner.Url;
        public long RequestContentLength => inner.RequestContentLength;
        public Stream RequestBody => inner.RequestBody; public Stream ResponseBody => inner.ResponseBody;
        public int StatusCode { get => inner.StatusCode; set => inner.StatusCode = value; }
        public string? ContentType { get => inner.ContentType; set => inner.ContentType = value; }
        public long ResponseContentLength { set => inner.ResponseContentLength = value; }
        public void SetResponseHeader(string name, string value) => inner.SetResponseHeader(name, value);
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { try { inner.Dispose(); } finally { Interlocked.Decrement(ref owner.Contexts); } } }
    }
}
