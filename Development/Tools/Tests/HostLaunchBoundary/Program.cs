using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BEngine;
using BEngine.Hosting;
using BEngine.Networking;
using BEngine.Platform.DotNet;
using BEngine.Testing;
using BEngine.Testing.Editor;

if (Environment.GetEnvironmentVariable("BENGINE_HOST_LAUNCH_ADB_STUB") is { Length: > 0 } stubLog)
{
    File.AppendAllText(stubLog, JsonSerializer.Serialize(args) + "\n");
    if (args.Contains("resolve-activity")) Console.WriteLine("com.bengine.boundaryfixture/.MainActivity");
    if (args.Contains("exec-out")) Console.WriteLine(File.ReadAllText(stubLog + ".result.json"));
    return;
}
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
DotNetPlatformServices.Initialize();
BEngine.Platform.DotNetEditorPlatform.Initialize();
var checks = new List<string>();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks.Add(name);
}
void Throws(Action action, string name)
{
    try { action(); }
    catch (InvalidDataException) { Check(true, name); return; }
    throw new InvalidOperationException(name + " was accepted");
}
var storage = Path.Combine(output, "PrivateFiles");
var cases = new List<object>();
string Encoded(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
var values = new Dictionary<string, string>
{
    ["GAME_OPTION"] = "中文/space \"quote\"\nline",
    ["EMPTY"] = "",
    ["__proto__"] = "ordinary environment key"
};
var paths = new Dictionary<string, string> { ["OUTPUT_FILE"] = "reports/result.json" };
var encoded = HostLaunchEnvironment.Encode(values, paths);
var decoded = HostLaunchEnvironment.Decode(encoded, storage);
Check(values.All(pair => decoded[pair.Key] == pair.Value), "Portable environment preserves Unicode, quotes, newlines and empty strings");
Check(decoded["OUTPUT_FILE"] == Path.Combine(storage, "reports", "result.json"), "Host resolves relative files inside its private directory");
Check(!encoded.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')), "Launcher payload is one shell-safe URL-safe argument");
cases.Add(new { Encoded = encoded, Valid = true, Environment = values, RelativeFiles = paths });
var empty = HostLaunchEnvironment.Encode(new Dictionary<string, string>());
Check(HostLaunchEnvironment.Decode(empty, storage).Count == 0, "Empty portable environment is valid");
cases.Add(new { Encoded = empty, Valid = true, Environment = new Dictionary<string, string>() });
var repeated = Encoded("{\"version\":1,\"environment\":{\"VALUE\":\"first\",\"VALUE\":\"last\"}}");
Check(HostLaunchEnvironment.Decode(repeated, storage)["VALUE"] == "last", "Repeated JSON properties use the final value consistently");
cases.Add(new { Encoded = repeated, Valid = true, Environment = new Dictionary<string, string> { ["VALUE"] = "last" } });
var overwritten = Encoded("{\"version\":1,\"environment\":{\"VALUE\":null,\"VALUE\":\"last\"}}");
Check(HostLaunchEnvironment.Decode(overwritten, storage)["VALUE"] == "last", "Overwritten values follow browser duplicate-property semantics");
cases.Add(new { Encoded = overwritten, Valid = true, Environment = new Dictionary<string, string> { ["VALUE"] = "last" } });
var invalidJson = new[]
{
    "null", "[]", "{}", "{\"version\":2}", "{\"version\":\"1\"}",
    "{\"version\":1,\"unknown\":{}}", "{\"version\":1,\"environment\":null}",
    "{\"version\":1,\"environment\":[]}", "{\"version\":1,\"environment\":{\"BAD=KEY\":\"x\"}}",
    "{\"version\":1,\"environment\":{\"1BAD\":\"x\"}}", "{\"version\":1,\"environment\":{\"GOOD\":42}}",
    "{\"version\":1,\"environment\":{\"GOOD\":\"bad\\u0000value\"}}",
    "{\"version\":1,\"environment\":{\"FILE\":\"x\"},\"relativeFiles\":{\"FILE\":\"y\"}}"
};
foreach (var json in invalidJson)
{
    var payload = Encoded(json);
    Throws(() => HostLaunchEnvironment.Decode(payload, storage), "Malformed launch data rejected: " + json);
    cases.Add(new { Encoded = payload, Valid = false });
}
foreach (var path in new[] { "", ".", "..", "a/../b", "/absolute", "a//b", "a/./b", "C:/root", "a\\b", "bad\npath", "a?b" })
{
    Throws(() => HostLaunchEnvironment.Encode(values, new Dictionary<string, string> { ["FILE"] = path }), "Encoder rejects unsafe relative path: " + JsonSerializer.Serialize(path));
    var payload = Encoded(JsonSerializer.Serialize(new { version = 1, relativeFiles = new Dictionary<string, string> { ["FILE"] = path } }));
    Throws(() => HostLaunchEnvironment.Decode(payload, storage), "Decoder rejects unsafe relative path: " + JsonSerializer.Serialize(path));
    cases.Add(new { Encoded = payload, Valid = false });
}
foreach (var payload in new[] { "A", "a+b", "a/b", "YQ==", "bad space", "_w" })
{
    Throws(() => HostLaunchEnvironment.Decode(payload, storage), "Invalid binary payload rejected: " + payload);
    cases.Add(new { Encoded = payload, Valid = false });
}
Throws(() => HostLaunchEnvironment.Encode(new Dictionary<string, string> { ["VALUE"] = new string('a', 1024 * 1024) }), "Encoder bounds launch payload size");
var large = Encoded(JsonSerializer.Serialize(new { version = 1, environment = new { VALUE = new string('a', 1024 * 1024) } }));
Throws(() => HostLaunchEnvironment.Decode(large, storage), "Decoder bounds launch payload size");
cases.Add(new { Encoded = large, Valid = false });
const string mutationKey = "BENGINE_LAUNCH_ATOMIC_PROBE";
var oldMutation = Environment.GetEnvironmentVariable(mutationKey);
try
{
    Environment.SetEnvironmentVariable(mutationKey, "before");
    Throws(() => HostLaunchEnvironment.Apply(Encoded("{\"version\":1,\"environment\":{\"BENGINE_LAUNCH_ATOMIC_PROBE\":\"after\",\"BAD=NAME\":\"x\"}}"), storage), "Apply validates every variable before mutating the process");
    Check(Environment.GetEnvironmentVariable(mutationKey) == "before", "Invalid launch payload cannot partially change process environment");
}
finally { Environment.SetEnvironmentVariable(mutationKey, oldMutation); }
File.WriteAllText(Path.Combine(output, "WireCases.json"), JsonSerializer.Serialize(cases));

var options = new PlayerTestLaunchOptions
{
    EnvironmentVariables = new Dictionary<string, string> { ["CUSTOM_INPUT"] = "preserved", ["BENGINE_TEST_RUNNER_COVERAGE"] = "wrong" },
    ContentUrlEnvironmentVariable = "CUSTOM_CONTENT_ENDPOINT"
};
var build = new PlayerTestBuild(null!, output, output) { LaunchOptions = options };
var launch = new PlayerTestLaunchRequest(build, "http://127.0.0.1:18765/main", Path.Combine(output, "results.json"), true, ["one", "two中文"]);
var factory = typeof(WebPlayerTestLauncher).Assembly.GetType("BEngine.Testing.Editor.PlayerTestLaunchEnvironment")!
    .GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!;
var inputs = (Dictionary<string, string>)factory.Invoke(null, [launch])!;
Check(inputs["CUSTOM_INPUT"] == "preserved" && inputs["CUSTOM_CONTENT_ENDPOINT"] == launch.ContentUrl, "All launchers retain arbitrary provider inputs and its selected content variable");
Check(inputs["BENGINE_TEST_RUNNER_COVERAGE"] == "1" && inputs["BENGINE_TEST_RUNNER_MODE"] == "player", "Testing owns its protocol values");
Check(JsonSerializer.Deserialize<string[]>(inputs["BENGINE_TEST_RUNNER_IDS"])!.SequenceEqual(launch.TestIds!), "Test IDs retain Unicode and exact filtering");
Check(options.EnvironmentVariables["BENGINE_TEST_RUNNER_COVERAGE"] == "wrong", "Launch environment composition never mutates the provider snapshot");

var previousTransport = NetworkTransportRegistry.HttpTransport;
var reportWriter = typeof(PlayerTestRuntimeSystem).Assembly.GetType("BEngine.Testing.PlayerTestReportJson", true)!
    .GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic)!;
var sample = new TestRunResult(DateTimeOffset.Parse("2026-01-02T03:04:05+08:00"), DateTimeOffset.UtcNow,
    Enum.GetValues<TestStatus>().Select(status => new TestResult("id/中文", "display\"name", TestMode.Player, status,
        1.25, "message\nnext", "stack", "Assets/代码.cs", 12)).ToArray(), null);
void ReportMatches(TestRunResult value, string name)
{
    var bytes = (byte[])reportWriter.Invoke(null, [value])!;
    Check(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(bytes),
        System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(value))), name);
}
ReportMatches(sample, "Reflection-free Player report preserves every result status, Unicode, escapes and null coverage");
ReportMatches(sample with { Coverage = new CoverageSnapshot(DateTimeOffset.UtcNow,
    [new CoverageFile("Assets/代码.cs", [new CoverageLine(12, 2), new CoverageLine(13, 0)])
        { Branches = [new CoverageBranch(12, 0, 1, 2), new CoverageBranch(12, 0, 2, 0)] }, new CoverageFile("empty.cs", [])]) },
    "Reflection-free Player report preserves line/branch coverage and all computed summaries");
var variables = new[] { "BENGINE_TEST_RUNNER_MODE", "BENGINE_TEST_RUNNER_RESULT", "BENGINE_TEST_RUNNER_RESULT_URL", "BENGINE_TEST_RUNNER_IDS", "BENGINE_TEST_RUNNER_COVERAGE" };
var old = variables.ToDictionary(key => key, Environment.GetEnvironmentVariable);
var quitAttempts = 0;
bool HoldQuit() { quitAttempts++; return false; }
Application.wantsToQuit += HoldQuit;
try
{
    var pass = TestDiscovery.Discover([typeof(Fixture).Assembly]).Single(test => test.MethodName == nameof(Fixture.Pass));
    void Configure(string? path, string? endpoint)
    {
        Environment.SetEnvironmentVariable(variables[0], "player");
        Environment.SetEnvironmentVariable(variables[1], path);
        Environment.SetEnvironmentVariable(variables[2], endpoint);
        Environment.SetEnvironmentVariable(variables[3], JsonSerializer.Serialize(new[] { pass.Id }));
        Environment.SetEnvironmentVariable(variables[4], "0");
    }
    void Pump(PlayerTestRuntimeSystem system, int frames = 4)
    {
        for (var index = 0; index < frames; index++)
        { system.Update(null!, 0); BTaskScheduler.Main.Tick(TimeSpan.FromMilliseconds(16)); }
    }
    var transport = new ManualTransport();
    NetworkTransportRegistry.HttpTransport = transport;
    Environment.SetEnvironmentVariable(variables[0], null);
    var inactive = new PlayerTestRuntimeSystem(); Pump(inactive);
    Check(transport.Sent == 0 && quitAttempts == 0 && Fixture.Calls == 0, "A regular Player never starts test execution or result transport");

    Configure(null, "http://127.0.0.1:18765/report");
    var remote = new PlayerTestRuntimeSystem(); Pump(remote, 8);
    Check(Fixture.Calls == 1 && transport.Sent == 1 && quitAttempts == 0, "Test body runs once; Player stays alive until report acknowledgement");
    Check(transport.Request!.Method == HttpRequestMethod.Post && transport.Request.UploadContentType == "application/json", "Testing sends its report through the shared Networking API");
    var report = JsonSerializer.Deserialize<TestRunResult>(transport.Request.UploadData!)!;
    Check(report.Passed == 1 && report.Failed == 0, "Network report contains the actual filtered test result");
    transport.Current!.Complete(HttpStatusCode.OK); Pump(remote);
    Check(quitAttempts == 1 && transport.Sent == 1, "Successful acknowledgement allows exactly one Player quit");

    var localPath = Path.Combine(output, "Local", "result.json");
    Configure(localPath, null);
    var local = new PlayerTestRuntimeSystem(); Pump(local);
    Check(quitAttempts == 2 && File.Exists(localPath) && JsonSerializer.Deserialize<TestRunResult>(File.ReadAllText(localPath))!.Passed == 1,
        "Local and device reports still write the selected file without HTTP");
    Check(transport.Sent == 1, "File-only reporting does not start a network request");

    Configure(null, "http://127.0.0.1:18765/report");
    var failed = new PlayerTestRuntimeSystem(); Pump(failed);
    transport.Current!.Complete(HttpStatusCode.InternalServerError); Pump(failed);
    Check(quitAttempts == 3 && transport.Sent == 2, "Failed report acknowledgement completes without retrying test execution");
    var stopped = new PlayerTestRuntimeSystem(); Pump(stopped);
    var active = transport.Current!;
    stopped.Stop(null!); Pump(stopped);
    Check(active.Aborted && quitAttempts == 3, "Scene stop releases an in-flight report without quitting a different scene");

    var latePath = Path.Combine(output, "Late", "result.json");
    if (File.Exists(latePath)) File.Delete(latePath);
    var lateId = "LatePlayerTests:LateFixture.Pass:Player";
    Configure(latePath, null);
    Environment.SetEnvironmentVariable(variables[3], JsonSerializer.Serialize(new[] { lateId }));
    var waiting = new PlayerTestRuntimeSystem(); Pump(waiting);
    Check(!File.Exists(latePath) && quitAttempts == 3, "Selected tests absent during AOT startup never produce an empty successful report");
    var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < 1000; i++) waiting.Update(null!, 0);
    Check(GC.GetAllocatedBytesForCurrentThread() == allocatedBefore, "Waiting for a selected assembly allocates zero bytes across 1000 unchanged frames");
    var lateAssembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("LatePlayerTests"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
    Pump(waiting);
    Check(!File.Exists(latePath) && quitAttempts == 3, "An early assembly notification before types exist does not complete selected tests");
    var lateType = lateAssembly.DefineDynamicModule("main").DefineType("LateFixture", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    var lateMethod = lateType.DefineMethod("Pass", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
    lateMethod.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(typeof(TestAttribute).GetConstructor([typeof(TestMode)])!, [TestMode.Player]));
    var il = lateMethod.GetILGenerator(); il.Emit(System.Reflection.Emit.OpCodes.Call, typeof(Fixture).GetMethod(nameof(Fixture.Pass))!); il.Emit(System.Reflection.Emit.OpCodes.Ret);
    lateType.CreateType();
    using var typePreference = RuntimeTypeCache.PreferAssemblies([typeof(Fixture).Assembly]);
    Pump(waiting);
    var lateResult = JsonSerializer.Deserialize<TestRunResult>(File.ReadAllText(latePath))!;
    Check(lateResult.Passed == 1 && lateResult.Tests.Single().TestId == lateId && quitAttempts == 4,
        "Runtime type-index activation resumes discovery without another CLR load event and executes the exact late test once");
    var observing = typeof(PlayerTestRuntimeSystem).GetField("_observingAssemblies", BindingFlags.Instance | BindingFlags.NonPublic)!;
    Check(!(bool)observing.GetValue(waiting)!, "Starting a discovered run releases its assembly-load observation");
    Environment.SetEnvironmentVariable(variables[3], JsonSerializer.Serialize(new[] { "never-loaded" }));
    var abandoned = new PlayerTestRuntimeSystem(); Pump(abandoned); abandoned.Stop(null!);
    Check(!(bool)observing.GetValue(abandoned)! && quitAttempts == 4, "Stopping a pending scene releases discovery observation without a report or quit");
}
finally
{
    Application.wantsToQuit -= HoldQuit;
    NetworkTransportRegistry.HttpTransport = previousTransport;
    foreach (var key in variables) Environment.SetEnvironmentVariable(key, old[key]);
}
await LaunchIntegrationTests.Run(output, args[1], Check);
File.WriteAllText(Path.Combine(output, "Result.json"), JsonSerializer.Serialize(new { Passed = true, Checks = checks.Count, Tests = checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"HOST_LAUNCH_BOUNDARY_OK|checks={checks.Count}");

public static class Fixture
{
    public static int Calls;
    [Test(TestMode.Player)] public static void Pass() => Calls++;
}
sealed class ManualTransport : IHttpTransport
{
    public int Sent;
    public HttpTransportRequest? Request;
    public ManualOperation? Current;
    public IHttpTransportOperation Send(HttpTransportRequest request, IHttpDownloadHandler handler)
    { Sent++; Request = request; return Current = new ManualOperation(handler); }
}
sealed class ManualOperation(IHttpDownloadHandler handler) : IHttpTransportOperation
{
    public bool Aborted;
    public NetworkOperationStatus Status { get; private set; }
    public Exception? Exception => null;
    public HttpTransportResponse? Response { get; private set; }
    public long DownloadedBytes => 0;
    public long? ContentLength => 0;
    public void Poll() { }
    public void Complete(HttpStatusCode status)
    {
        Response = new(status, status.ToString(), new Dictionary<string, string[]>());
        handler.ReceiveResponse(Response);
        Status = NetworkOperationStatus.Succeeded;
    }
    public void Abort() { Aborted = true; Status = NetworkOperationStatus.Failed; }
    public void Dispose() { }
}
