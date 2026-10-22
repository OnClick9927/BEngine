namespace BEngine.Jobs;

public readonly record struct JobSystemSnapshot(int WorkerCount, long ScheduledJobs, long CompletedJobs,
    int ActiveWorkers, int PeakActiveWorkers)
{
    public string ExecutionMode => "SingleThreadAsync";
    public long PendingJobs => ScheduledJobs - CompletedJobs;
    public double LongestStepMilliseconds { get; init; }
    public double LongestCompletionMilliseconds { get; init; }
}

/// <summary>Dependency-aware cooperative jobs on BEngine's single-thread asynchronous frame loop.</summary>
public static class JobSystem
{
    static JobSystem() => Diagnostics.RuntimeDiagnosticRegistry.Register("budget.jobs", "Cooperative Jobs",
        new("pending", "Pending Jobs", Diagnostics.RuntimeDiagnosticUnit.Number, () => snapshot.PendingJobs),
        new("longest", "Longest Job step", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => snapshot.LongestStepMilliseconds),
        new("latency", "Longest completion latency", Diagnostics.RuntimeDiagnosticUnit.Milliseconds, () => snapshot.LongestCompletionMilliseconds));
    private static CooperativeJobScheduler? _scheduler;
    private static long _nextJobId, _scheduled, _completed;
    public static int workerCount => 0;
    private static int _maximumPendingJobs = 8192;
    private static double _longestStep, _longestCompletion;
    public static int maximumPendingJobs
    { get => _maximumPendingJobs; set { if (value is < 1 or > 100000) throw new ArgumentOutOfRangeException(nameof(value)); _maximumPendingJobs = value; } }
    public static JobSystemSnapshot snapshot => new(0, _scheduled, _completed, 0, 0)
    { LongestStepMilliseconds = _longestStep, LongestCompletionMilliseconds = _longestCompletion };
    internal static void RecordStep(double milliseconds) => _longestStep = Math.Max(_longestStep, milliseconds);
    internal static void RecordLatency(double milliseconds) => _longestCompletion = Math.Max(_longestCompletion, milliseconds);

    /// <param name="workerCount">Legacy compatibility argument. No background workers are created.</param>
    public static void Initialize(int workerCount = 0)
    {
        if (workerCount < 0) throw new ArgumentOutOfRangeException(nameof(workerCount));
        if (_scheduler is null || _scheduler.IsDisposed) _scheduler = new CooperativeJobScheduler();
        _scheduler.CheckOwner();
    }

    /// <summary>Cancels queued work and releases native-container reservations.</summary>
    public static void Shutdown()
    {
        BackgroundJobs.Shutdown();
        _scheduler?.Dispose();
        _scheduler = null;
    }

    internal static JobHandle Schedule<TJob>(TJob job, JobHandle dependency) where TJob : struct, IJob
    {
        _ = SingleJobDispatch<TJob>.Compilation;
        var copy = job;
        return ScheduleCore(JobContainerSafety.Collect(job), dependency, () =>
        { SingleJobDispatch<TJob>.Execute(ref copy); return true; });
    }

    internal static JobHandle ScheduleParallel<TJob>(TJob job, int length, int batchSize, JobHandle dependency)
        where TJob : struct, IJobParallelFor
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        _ = ParallelJobDispatch<TJob>.Compilation;
        var next = 0;
        return ScheduleCore(JobContainerSafety.Collect(job), dependency, () =>
        {
            var end = (int)Math.Min((long)next + batchSize, length);
            var copy = job;
            for (; next < end; next++) ParallelJobDispatch<TJob>.Execute(ref copy, next);
            return next == length;
        }, () => length == 0 ? 1 : (double)next / length);
    }

    private static JobHandle ScheduleCore(JobContainerAccess[] accesses, JobHandle dependency, Func<bool> step, Func<double>? progress = null)
    {
        var scheduler = Scheduler;
        scheduler.CheckOwner();
        var jobId = ++_nextJobId;
        JobContainerSafety.Reserve(accesses, jobId, dependency.DependencyIds);
        _scheduled++;
        var operation = new JobOperation(jobId, dependency.Operation is { } parent ? [parent] : [], step, () =>
        { JobContainerSafety.Release(accesses, jobId); _completed++; });
        operation.GetProgress = progress;
        try { scheduler.Enqueue(operation); }
        catch { operation.Release(); throw; }
        var ids = new HashSet<long>(dependency.DependencyIds) { jobId };
        return new(operation, ids);
    }

    internal static JobHandle Combine(ReadOnlySpan<JobHandle> handles)
    {
        var operations = new List<JobOperation>(); var ids = new HashSet<long>();
        foreach (var handle in handles)
        {
            if (handle.Operation is { } operation && !operations.Contains(operation)) operations.Add(operation);
            ids.UnionWith(handle.DependencyIds);
        }
        if (operations.Count == 0) return default;
        var combined = new JobOperation(0, operations.ToArray(), static () => true, static () => { });
        Scheduler.Enqueue(combined);
        return new(combined, ids);
    }

    internal static void Complete(JobOperation operation)
    {
        if (operation.Task.IsCompleted) { operation.Task.GetAwaiter().GetResult(); return; }
        Scheduler.Complete(operation);
    }

    private static CooperativeJobScheduler Scheduler
    {
        get { Initialize(); return _scheduler!; }
    }
}
