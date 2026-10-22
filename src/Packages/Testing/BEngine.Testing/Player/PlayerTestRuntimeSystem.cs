using System.Text.Json;

namespace BEngine.Testing;

public sealed class PlayerTestRuntimeSystem : ISceneRuntimeSystem
{
    private const string ModeVariable = "BENGINE_TEST_RUNNER_MODE";
    private const string ResultVariable = "BENGINE_TEST_RUNNER_RESULT";
    private Task<TestRunResult>? _run;
    private bool _finished;

    public string packageId => "com.bengine.testing";
    public int order => 9000;

    public void Update(Scene scene, Fix64 deltaTime)
    {
        _ = deltaTime;
        if (_finished || !string.Equals(Environment.GetEnvironmentVariable(ModeVariable),
                "player", StringComparison.OrdinalIgnoreCase)) return;
        if (_run is null)
        {
            var tests = TestDiscovery.Discover(AppDomain.CurrentDomain.GetAssemblies())
                .Where(test => test.Mode == TestMode.Player).ToArray();
            if (Environment.GetEnvironmentVariable("BENGINE_TEST_RUNNER_IDS") is { Length: > 0 } filter)
            {
                var ids = JsonSerializer.Deserialize<string[]>(filter) ?? [];
                tests = tests.Where(test => ids.Contains(test.Id, StringComparer.Ordinal)).ToArray();
            }
            var collectCoverage = string.Equals(Environment.GetEnvironmentVariable("BENGINE_TEST_RUNNER_COVERAGE"),
                "1", StringComparison.Ordinal);
            _run = TestExecutor.RunAsync(tests, new TestRunOptions
            {
                CollectCoverage = collectCoverage,
                PlayerScene = scene
            });
            return;
        }
        if (!_run.IsCompleted) return;
        _finished = true;
        try
        {
            var result = _run.GetAwaiter().GetResult();
            var path = Environment.GetEnvironmentVariable(ResultVariable);
            if (!string.IsNullOrWhiteSpace(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                File.WriteAllText(path, JsonSerializer.Serialize(result,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            Debug.Log($"BENGINE_PLAYER_TESTS_COMPLETED|passed={result.Passed}|failed={result.Failed}|skipped={result.Skipped}|coverage={result.Coverage?.CoveredLines ?? 0}/{result.Coverage?.TotalLines ?? 0}");
            if (!result.Succeeded) Debug.LogError("BENGINE_PLAYER_TESTS_FAILED|One or more Player tests failed.");
        }
        catch (Exception exception)
        {
            Debug.LogError("BENGINE_PLAYER_TESTS_FAILED|" + exception);
        }
        Application.Quit();
    }
}
