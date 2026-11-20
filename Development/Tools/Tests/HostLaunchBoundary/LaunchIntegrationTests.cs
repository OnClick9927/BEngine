using System.Text.Json;
using System.Text.RegularExpressions;
using BEngine.Build;
using BEngine.Hosting;
using BEngine.Testing;
using BEngine.Testing.Editor;

internal static class LaunchIntegrationTests
{
    internal static async Task Run(string output, string platformSource, Action<bool, string> check)
    {
        var web = Path.Combine(output, "BrowserFixture");
        Directory.CreateDirectory(web);
        File.WriteAllText(Path.Combine(web, "platform-fixture.js"),
            Regex.Replace(File.ReadAllText(platformSource), @"^import .*;\r?$", "", RegexOptions.Multiline));
        File.WriteAllText(Path.Combine(web, "index.html"), """
            <!doctype html><script type="module">
            import {decodeHostLaunchEnvironment} from './platform-fixture.js';
            const query = new URLSearchParams(location.search);
            const environment = decodeHostLaunchEnvironment(query.get('bengineEnvironment'));
            const content = query.has('contentProbe')
              ? await (await fetch(environment.CUSTOM_ENDPOINT + 'probe.bin', {
                  headers: {'X-BEngine-Test-Probe': 'preflight', 'Cache-Control': 'no-cache'}
                })).text() : null;
            const now = new Date().toISOString();
            const result = {StartedAt: now, FinishedAt: now, Coverage: null, Tests: [{
              TestId: 'browser-wire', DisplayName: 'browser wire', Mode: 2, Status: 2,
              DurationMilliseconds: 1, Message: JSON.stringify({environment, query: Object.fromEntries(query), content}),
              StackTrace: '', SourceFile: '', SourceLine: 0
            }]};
            await fetch(environment.BENGINE_TEST_RUNNER_RESULT_URL, {
              method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(result)
            });
            </script>
            """);
        var options = new PlayerTestLaunchOptions
        {
            EnvironmentVariables = new Dictionary<string, string> { ["CUSTOM_INPUT"] = "中文 \"quoted\"\nvalue" },
            ContentUrlEnvironmentVariable = "CUSTOM_ENDPOINT",
            BrowserQueryParameters = new Dictionary<string, string> { ["customQuery"] = "preserved" },
            AndroidBooleanExtras = new Dictionary<string, bool> { ["customFlag"] = true }
        };
        var target = new BuildTargetDescriptor("web", BuildTargetPlatform.Web, BuildArchitecture.Wasm32,
            "browser-wasm", default, [], true);
        var build = new PlayerTestBuild(target, web, web) { LaunchOptions = options };
        var request = new PlayerTestLaunchRequest(build, "http://127.0.0.1:18765/content", Path.Combine(output, "BrowserResult.json"),
            true, ["one", "two中文"]) { ProjectPath = output };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var browser = await WebPlayerTestLauncher.Run(request, timeout.Token);
        check(browser.Succeeded && browser.Passed == 1 && File.Exists(request.ResultPath),
            "Real Edge launch returns and persists the Testing-owned HTTP report");
        using var observation = JsonDocument.Parse(browser.Tests.Single().Message);
        var environment = observation.RootElement.GetProperty("environment");
        var query = observation.RootElement.GetProperty("query");
        check(environment.GetProperty("CUSTOM_INPUT").GetString() == options.EnvironmentVariables["CUSTOM_INPUT"] &&
              environment.GetProperty("CUSTOM_ENDPOINT").GetString() == request.ContentUrl,
            "Real browser receives arbitrary provider environment and selected content endpoint");
        check(environment.GetProperty("BENGINE_TEST_RUNNER_COVERAGE").GetString() == "1" &&
              JsonSerializer.Deserialize<string[]>(environment.GetProperty("BENGINE_TEST_RUNNER_IDS").GetString()!)!.SequenceEqual(request.TestIds!),
            "Real browser retains coverage and Unicode test filters");
        check(query.EnumerateObject().Select(pair => pair.Name).Order().SequenceEqual(new[] { "bengineEnvironment", "customQuery" }.Order()) &&
              query.GetProperty("customQuery").GetString() == "preserved",
            "Real browser uses one generic launch payload and preserves provider query parameters");
        var profileRoot = Path.Combine(output, "Temp", "TestRunner", "BrowserProfiles");
        check(!Directory.EnumerateDirectories(profileRoot).Any(), "Browser launcher removes its owned verification profile");

        var contentDirectory = Path.Combine(output, "ContentFixture");
        Directory.CreateDirectory(contentDirectory);
        File.WriteAllText(Path.Combine(contentDirectory, "probe.bin"), "真实跨域资源");
        var contentBuild = new PlayerTestBuild(target, web, contentDirectory)
        {
            LaunchOptions = new PlayerTestLaunchOptions
            {
                EnvironmentVariables = options.EnvironmentVariables,
                ContentUrlEnvironmentVariable = options.ContentUrlEnvironmentVariable,
                BrowserQueryParameters = new Dictionary<string, string> { ["contentProbe"] = "1" }
            }
        };
        var contentResult = await PlayerTestSession.RunProjectAsync(output, false, "web",
            cancellationToken: timeout.Token, buildProvider: new ExistingBuild(contentBuild));
        check(contentResult.Succeeded && contentResult.Passed == 1,
            "Real Edge Player session completes cross-origin content loading with CORS preflight");
        using var contentObservation = JsonDocument.Parse(contentResult.Tests.Single().Message);
        check(contentObservation.RootElement.GetProperty("content").GetString() == "真实跨域资源",
            "Preflight authorizes custom and cache-control headers before returning the unmodified resource");

        var adbLog = Path.Combine(output, "AdbWire.jsonl");
        File.WriteAllText(adbLog, "");
        File.WriteAllText(adbLog + ".result.json", JsonSerializer.Serialize(browser));
        File.WriteAllBytes(Path.Combine(web, "fixture.apk"), []);
        var keys = new[] { "BENGINE_TEST_ADB", "BENGINE_TEST_ANDROID_SERIAL", "BENGINE_HOST_LAUNCH_ADB_STUB" };
        var previous = keys.ToDictionary(key => key, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(keys[0], Environment.ProcessPath);
            Environment.SetEnvironmentVariable(keys[1], null);
            Environment.SetEnvironmentVariable(keys[2], adbLog);
            var androidRequest = request with { AndroidApplicationIdentifier = "com.bengine.boundaryfixture", ResultPath = Path.Combine(output, "AndroidResult.json") };
            var android = await AndroidPlayerTestLauncher.Run(androidRequest, timeout.Token);
            check(android.Passed == 1 && File.Exists(androidRequest.ResultPath), "Android launcher retrieves and persists its private result through ADB");
            var commands = File.ReadAllLines(adbLog).Select(line => JsonSerializer.Deserialize<string[]>(line)!).ToArray();
            var start = commands.Single(command => command.Contains("start"));
            var payloadIndex = Array.IndexOf(start, HostLaunchEnvironment.ArgumentName);
            var privateRoot = Path.Combine(output, "AndroidPrivateFiles");
            var deviceEnvironment = HostLaunchEnvironment.Decode(start[payloadIndex + 1], privateRoot);
            check(deviceEnvironment["CUSTOM_INPUT"] == options.EnvironmentVariables["CUSTOM_INPUT"] &&
                  deviceEnvironment["CUSTOM_ENDPOINT"] == request.ContentUrl && deviceEnvironment["BENGINE_TEST_RUNNER_COVERAGE"] == "1",
                "Android launch forwards provider inputs and Testing options through the generic payload");
            check(start.Contains("customFlag") && start.Count(argument => argument == "--es") == 1 && !start.Contains("bengineRunTests"),
                "Android retains provider booleans without host-specific test extras");
            var remote = commands.Single(command => command.Contains("cat"))[^1];
            check(deviceEnvironment["BENGINE_TEST_RUNNER_RESULT"] == Path.Combine(privateRoot, remote["files/".Length..]),
                "Android host resolves the requested report inside its private file directory");
            check(commands.Count(command => command.Contains("force-stop")) == 2 && commands.Any(command => command.Contains("--remove")) &&
                  commands.Any(command => command.Contains("rm") && command[^1] == remote),
                "Android launcher releases its process, reverse mapping and report (ADB fixture, no physical device)");
        }
        finally { foreach (var key in keys) Environment.SetEnvironmentVariable(key, previous[key]); }

        var provider = new CaptureProvider();
        try { await PlayerTestSession.RunProjectAsync(output, false, "web", buildProvider: provider); }
        catch (OperationCanceledException exception) when (ReferenceEquals(exception, provider.Stop)) { }
        var captured = provider.Request!;
        check(captured.OutputDirectory.StartsWith(Path.Combine(output, "Build", "TestRunner") + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Player test builds obey the engine's dedicated project Build directory rule");
        check(captured.ContentOutputDirectory.StartsWith(Path.Combine(output, "Temp", "TestRunner") + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Player test intermediate content remains in the owning project's Temp directory");
    }

    private sealed class CaptureProvider : IPlayerTestBuildProvider
    {
        internal readonly OperationCanceledException Stop = new("Fixture stops before building an artifact.");
        internal PlayerTestBuildRequest? Request;
        public Task<PlayerTestBuild> BuildAsync(PlayerTestBuildRequest request,
            IProgress<PlayerTestBuildProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromException<PlayerTestBuild>(Stop);
        }
    }

    private sealed class ExistingBuild(PlayerTestBuild build) : IPlayerTestBuildProvider
    {
        public Task<PlayerTestBuild> BuildAsync(PlayerTestBuildRequest request,
            IProgress<PlayerTestBuildProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(build);
        }
    }
}
