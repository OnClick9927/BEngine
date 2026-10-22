using System.Reflection;
using System.Text.Json.Serialization;

namespace BEngine.Testing;

public enum TestStatus
{
    NotRun,
    Running,
    Passed,
    Failed,
    Skipped,
    TimedOut,
    Canceled
}

public sealed class TestCase
{
    public required string Id { get; init; }
    public required string AssemblyName { get; init; }
    public required string TypeName { get; init; }
    public required string MethodName { get; init; }
    public required TestMode Mode { get; init; }
    public string Category { get; init; } = string.Empty;
    public int TimeoutMilliseconds { get; init; } = 10_000;
    public string IgnoreReason { get; init; } = string.Empty;
    public string SourceFile { get; set; } = string.Empty;
    public int SourceLine { get; set; }
    [JsonIgnore] public required MethodInfo Method { get; init; }
    [JsonIgnore] public Type FixtureType => Method.DeclaringType!;
    public string DisplayName => $"{TypeName}.{MethodName}";
}

public sealed record TestResult(
    string TestId,
    string DisplayName,
    TestMode Mode,
    TestStatus Status,
    double DurationMilliseconds,
    string Message,
    string StackTrace,
    string SourceFile,
    int SourceLine);

public sealed record TestRunResult(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<TestResult> Tests,
    CoverageSnapshot? Coverage)
{
    public int Passed => Tests.Count(item => item.Status == TestStatus.Passed);
    public int Failed => Tests.Count(item => item.Status is TestStatus.Failed or TestStatus.TimedOut);
    public int Skipped => Tests.Count(item => item.Status == TestStatus.Skipped);
    public bool Succeeded => Failed == 0 && Tests.All(item => item.Status != TestStatus.Canceled);
}

public sealed class TestRunOptions
{
    public bool CollectCoverage { get; init; }
    public Scene? PlayerScene { get; init; }
    public Action<TestResult>? ResultReported { get; init; }
}
