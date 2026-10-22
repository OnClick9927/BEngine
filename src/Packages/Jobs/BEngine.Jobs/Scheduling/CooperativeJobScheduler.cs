using System.Runtime.Loader;

namespace BEngine.Jobs;

/// <summary>Runs bounded batches on the owning BTask frame loop. Never creates worker threads.</summary>
internal sealed class CooperativeJobScheduler : IDisposable
{
    private readonly Queue<JobOperation> _queue = new();
    private readonly HashSet<JobOperation> _pending = [];
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private AssemblyLoadContext? _context;
    private bool _subscribed, _disposed;
    private IDisposable? _work;
    internal const int BatchesPerFrame = 64;
    internal bool IsDisposed => _disposed;

    internal void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Jobs must be scheduled and completed on the owning engine thread.");
    }

    internal void Enqueue(JobOperation operation)
    {
        CheckOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pending.Count >= JobSystem.maximumPendingJobs) throw new InvalidOperationException("The cooperative Job queue is full.");
        if (!_subscribed)
        {
            _work = CooperativeWorkScheduler.Main.Schedule(Advance, error =>
            {
                foreach (var queued in _pending.ToArray()) if (!queued.Running) Finish(queued, error);
                _queue.Clear(); Unsubscribe();
            });
        }
        _pending.Add(operation); _queue.Enqueue(operation);
        if (_subscribed) return;
        _subscribed = true;
        Application.quitting += Dispose;
        _context = AssemblyLoadContext.GetLoadContext(typeof(CooperativeJobScheduler).Assembly);
        if (_context?.IsCollectible == true) _context.Unloading += Unloading;
    }

    internal void Complete(JobOperation operation)
    {
        CheckOwner();
        while (!operation.Task.IsCompleted)
        {
            if (!RunPass(BatchesPerFrame))
                throw new InvalidOperationException("This Job cannot complete while its dependency is executing. Await the handle after returning from the current Job.");
        }
        operation.Task.GetAwaiter().GetResult();
    }

    private CooperativeWorkStatus Advance()
    {
        var progressed = RunPass(1, 64);
        return _pending.Count == 0 ? CooperativeWorkStatus.Complete :
            progressed ? CooperativeWorkStatus.Continue : CooperativeWorkStatus.Blocked;
    }

    private bool RunPass(int budget, int maximumExamined = int.MaxValue)
    {
        var progressed = false;
        var blocked = 0;
        var processed = 0;
        var examined = 0;
        while (processed < budget && examined++ < maximumExamined && _queue.TryDequeue(out var operation))
        {
            if (operation.Task.IsCompleted) continue;
            if (!operation.Dependencies.All(dependency => dependency.Task.IsCompleted))
            {
                _queue.Enqueue(operation);
                if (++blocked >= _queue.Count) break;
                continue;
            }
            blocked = 0; processed++;
            progressed = true;
            Exception? failure = null;
            var previous = JobExecutionContext.CurrentJobId;
            var finished = false;
            operation.Running = true;
            operation.StartedAt ??= System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                foreach (var dependency in operation.Dependencies) dependency.Task.GetAwaiter().GetResult();
                JobExecutionContext.CurrentJobId = operation.Id;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                try { finished = operation.Step(); }
                finally { JobSystem.RecordStep(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
            }
            catch (Exception error) { failure = error; finished = true; }
            finally { JobExecutionContext.CurrentJobId = previous; operation.Running = false; }
            if (_disposed && !finished) failure = new BOperationCanceledException(default);
            if (finished || failure is not null) Finish(operation, failure);
            else _queue.Enqueue(operation);
        }
        if (_pending.Count == 0) Unsubscribe();
        return progressed || examined > maximumExamined;
    }

    private void Finish(JobOperation operation, Exception? failure)
    {
        if (!_pending.Remove(operation)) return;
        operation.Release();
        JobSystem.RecordLatency(System.Diagnostics.Stopwatch.GetElapsedTime(operation.QueuedAt).TotalMilliseconds);
        if (failure is null) operation.Source.TrySetResult();
        else operation.Source.TrySetException(failure);
    }

    private void Unloading(AssemblyLoadContext _) => Dispose();

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        _work?.Dispose(); _work = null;
        Application.quitting -= Dispose;
        if (_context?.IsCollectible == true) _context.Unloading -= Unloading;
        _context = null;
    }

    public void Dispose()
    {
        CheckOwner();
        if (_disposed) return;
        _disposed = true; Unsubscribe();
        foreach (var operation in _pending.ToArray())
            if (!operation.Running) Finish(operation, new BOperationCanceledException(default));
        _queue.Clear();
    }
}

internal sealed class JobOperation(long id, JobOperation[] dependencies, Func<bool> step, Action release)
{
    internal long Id { get; } = id;
    internal JobOperation[] Dependencies { get; } = dependencies;
    internal Func<bool> Step { get; } = step;
    internal Action Release { get; } = release;
    internal BTaskCompletionSource Source { get; } = new();
    internal BTask Task => Source.Task;
    internal bool Running { get; set; }
    internal long QueuedAt { get; } = System.Diagnostics.Stopwatch.GetTimestamp();
    internal long? StartedAt { get; set; }
    internal Func<double>? GetProgress { get; set; }
}
