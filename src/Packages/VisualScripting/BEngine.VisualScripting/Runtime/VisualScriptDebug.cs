namespace BEngine.VisualScripting;

public enum VisualScriptExecutionStatus { Idle, Running, Paused, Completed, Faulted, Waiting }

public sealed record VisualScriptTraceEntry(
    Guid RunnerId,
    string EventName,
    string NodeId,
    VisualScriptNodeKind NodeKind,
    int Step,
    DateTimeOffset Timestamp);

public sealed class VisualScriptDebugSnapshot
{
    public required Guid RunnerId { get; init; }
    public required string RunnerName { get; init; }
    public required VisualScriptExecutionStatus Status { get; init; }
    public required string EventName { get; init; }
    public required string CurrentNodeId { get; init; }
    public required int StepCount { get; init; }
    public required string Error { get; init; }
    public required IReadOnlyDictionary<string, VisualScriptValue> Variables { get; init; }
}

public static class VisualScriptDebugHub
{
    private static readonly object Gate = new();
    private static readonly Dictionary<Guid, WeakReference<VisualScriptRunner>> Runners = [];

    public static event Action<VisualScriptTraceEntry>? tracePublished;
    public static event Action<VisualScriptDebugSnapshot>? stateChanged;

    public static IReadOnlyList<VisualScriptRunner> GetRunners()
    {
        lock (Gate)
        {
            RemoveDead();
            return Runners.Values.Select(reference =>
                    reference.TryGetTarget(out var runner) ? runner : null)
                .Where(runner => runner is not null)
                .Cast<VisualScriptRunner>()
                .OrderBy(runner => runner.gameObject.name, StringComparer.Ordinal)
                .ThenBy(runner => runner.Id)
                .ToArray();
        }
    }

    public static VisualScriptRunner? Find(Guid id)
    {
        lock (Gate)
            return Runners.TryGetValue(id, out var reference) && reference.TryGetTarget(out var runner)
                ? runner
                : null;
    }

    internal static void Register(VisualScriptRunner runner)
    {
        lock (Gate) Runners[runner.Id] = new WeakReference<VisualScriptRunner>(runner);
        PublishState(runner.CreateDebugSnapshot());
    }

    internal static void Unregister(VisualScriptRunner runner)
    {
        lock (Gate) Runners.Remove(runner.Id);
    }

    internal static void PublishTrace(VisualScriptTraceEntry trace) => tracePublished?.Invoke(trace);
    internal static void PublishState(VisualScriptDebugSnapshot snapshot) => stateChanged?.Invoke(snapshot);

    private static void RemoveDead()
    {
        foreach (var id in Runners.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray())
            Runners.Remove(id);
    }
}
