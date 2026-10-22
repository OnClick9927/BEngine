namespace BEngine.Jobs;

public readonly struct JobHandle
{
    private readonly JobOperation? _operation;
    private readonly IReadOnlySet<long>? _dependencyIds;

    internal JobHandle(JobOperation operation, IReadOnlySet<long> dependencyIds)
    {
        _operation = operation;
        _dependencyIds = dependencyIds;
    }

    public bool IsCompleted => Task.IsCompleted;
    public bool IsCompletedSuccessfully => Task.IsCompletedSuccessfully;
    public double Progress => IsCompletedSuccessfully ? 1 : _operation?.GetProgress?.Invoke() ?? 0;
    public double QueuedMilliseconds => _operation is { StartedAt: null } operation && !IsCompleted
        ? System.Diagnostics.Stopwatch.GetElapsedTime(operation.QueuedAt).TotalMilliseconds : 0;
    internal JobOperation? Operation => _operation;
    internal BTask Task => _operation?.Task ?? BTask.CompletedTask;
    internal IReadOnlySet<long> DependencyIds => _dependencyIds ?? EmptyIds.Instance;

    public void Complete() { if (_operation is { } operation) JobSystem.Complete(operation); }
    public BTask CompleteAsync() => Task;
    public BTaskAwaiter GetAwaiter() => Task.GetAwaiter();

    public static JobHandle CombineDependencies(params ReadOnlySpan<JobHandle> handles)
    {
        return JobSystem.Combine(handles);
    }

    public static void CompleteAll(params ReadOnlySpan<JobHandle> handles)
    {
        CombineDependencies(handles).Complete();
    }

    private sealed class EmptyIds : HashSet<long>
    {
        internal static readonly EmptyIds Instance = [];
    }
}
