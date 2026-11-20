using System.Collections.Concurrent;
using System.Diagnostics;

namespace BEngine.Editor;

public sealed class EditorTaskScheduler : IEditorTaskScheduler
{
    private const int CriticalBurstLimit = 8;
    private const int NormalBurstLimit = 4;
    private readonly ConcurrentQueue<IEditorWorkItem> _critical = new();
    private readonly ConcurrentQueue<IEditorWorkItem> _normal = new();
    private readonly ConcurrentQueue<IEditorWorkItem> _background = new();
    private readonly ConcurrentQueue<EditorMainThreadWorkItem> _mainThread = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private readonly Lock _lifecycleGate = new();
    private readonly ExecutionUtility _executionEnvironment;
    private readonly int _mainThreadId;
    private int _pendingBackground;
    private int _disposed;

    public EditorTaskScheduler(int? workerCount = null)
        : this(EditorHostServices.Execution, workerCount)
    {
    }

    public EditorTaskScheduler(
        ExecutionUtility executionEnvironment,
        int? workerCount = null)
    {
        _executionEnvironment = executionEnvironment ??
                                throw new ArgumentNullException(nameof(executionEnvironment));
        _mainThreadId = _executionEnvironment.CurrentManagedThreadId;
        WorkerCount = workerCount ?? Math.Clamp(_executionEnvironment.ProcessorCount - 2, 1, 8);
        if (WorkerCount < 1)
            throw new ArgumentOutOfRangeException(nameof(workerCount), "At least one worker is required.");
        _workers = Gather1638(Enumerable.Range(0, WorkerCount));
        [global::BEngine.SkipLoopGuard]
        global::System.Threading.Tasks.Task[] Gather1638(global::System.Collections.Generic.IEnumerable<int> items1638)
        {
            ArgumentNullException.ThrowIfNull(items1638, "source");
            global::System.Threading.Tasks.Task Evaluate1638(int _) => Task.Run(WorkerLoopAsync);
            global::System.ReadOnlySpan<int> values;
            if (items1638.GetType() == typeof(int[]))
                values = (int[])items1638;
            else if (items1638.GetType() == typeof(global::System.Collections.Generic.List<int>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<int>)items1638);
            else
            {
                return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items1638, Evaluate1638));
            }

            if (values.IsEmpty)
                return global::System.Array.Empty<global::System.Threading.Tasks.Task>();
            var result = new global::System.Threading.Tasks.Task[values.Length];
            for (var index = 0; index < values.Length; index++)
                result[index] = Evaluate1638(values[index]);
            return result;
        }
    }

    public int WorkerCount { get; }
    public bool IsMainThread => _executionEnvironment.CurrentManagedThreadId == _mainThreadId;
    public int PendingBackgroundCount => Volatile.Read(ref _pendingBackground);
    public int PendingMainThreadCount => _mainThread.Count;

    public Task ScheduleAsync(
        string name,
        Action<CancellationToken> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ScheduleAsync(name, token =>
        {
            operation(token);
            return true;
        }, priority, cancellationToken);
    }

    public Task<T> ScheduleAsync<T>(
        string name,
        Func<CancellationToken, T> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Enqueue(name, token => ValueTask.FromResult(operation(token)), priority, cancellationToken);
    }

    public Task ScheduleAsync(
        string name,
        Func<CancellationToken, Task> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Enqueue(name, async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, priority, cancellationToken);
    }

    public Task<T> ScheduleAsync<T>(
        string name,
        Func<CancellationToken, Task<T>> operation,
        EditorTaskPriority priority = EditorTaskPriority.Normal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Enqueue(name, async token => await operation(token).ConfigureAwait(false),
            priority, cancellationToken);
    }

    public void Post(Action callback, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            _mainThread.Enqueue(new EditorMainThreadWorkItem(
                string.IsNullOrWhiteSpace(name) ? "Main-thread callback" : name, callback));
        }
    }

    public int PumpMainThread(TimeSpan timeBudget, int maxCallbacks = 128)
    {
        if (!IsMainThread)
            throw new InvalidOperationException("Editor main-thread callbacks must be pumped by the owning thread.");
        if (maxCallbacks < 1) throw new ArgumentOutOfRangeException(nameof(maxCallbacks));
        if (timeBudget < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeBudget));

        var started = EditorHostServices.Platform.MonotonicTimestamp;
        var processed = 0;
        while (processed < maxCallbacks && _mainThread.TryDequeue(out var item))
        {
            EditorCallbacks.Invoke($"Background result {item.Name}", item.Callback);
            processed++;
            if (EditorHostServices.Platform.GetElapsedTime(started) >= timeBudget) break;
        }
        return processed;
    }

    public void Dispose()
    {
        if (!IsMainThread)
            throw new InvalidOperationException(
                "The Editor background scheduler must be disposed by its owning main thread.");
        lock (_lifecycleGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            CancelQueued(_critical);
            CancelQueued(_normal);
            CancelQueued(_background);
            _mainThread.Clear();
        }
        _shutdown.Cancel();
        _signal.Release(WorkerCount);
        try { Task.WaitAll(_workers); }
        catch (AggregateException) { }
        _shutdown.Dispose();
        _signal.Dispose();
        GC.SuppressFinalize(this);
    }

    private Task<T> Enqueue<T>(
        string name,
        Func<CancellationToken, ValueTask<T>> operation,
        EditorTaskPriority priority,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(priority)) throw new ArgumentOutOfRangeException(nameof(priority));
        var item = new EditorWorkItem<T>(name, operation, priority, cancellationToken);
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            Queue(priority).Enqueue(item);
            Interlocked.Increment(ref _pendingBackground);
            _signal.Release();
        }
        return item.Task;
    }

    private async Task WorkerLoopAsync()
    {
        var criticalBurst = 0;
        var normalBurst = 0;
        try
        {
            while (true)
            {
                await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                if (!TryDequeue(ref criticalBurst, ref normalBurst, out var item)) continue;
                Interlocked.Decrement(ref _pendingBackground);
                await item.ExecuteAsync(_shutdown.Token).ConfigureAwait(false);
                if (item.Completion.Exception?.GetBaseException() is { } exception)
                {
                    TryPostFailure(item.Name, exception);
                    _ = item.Completion.Exception;
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
    }

    private bool TryDequeue(ref int criticalBurst, ref int normalBurst, out IEditorWorkItem item)
    {
        if (criticalBurst < CriticalBurstLimit && _critical.TryDequeue(out item!))
        {
            criticalBurst++;
            return true;
        }
        if (normalBurst < NormalBurstLimit && _normal.TryDequeue(out item!))
        {
            criticalBurst = 0;
            normalBurst++;
            return true;
        }
        if (_background.TryDequeue(out item!))
        {
            criticalBurst = 0;
            normalBurst = 0;
            return true;
        }
        if (_critical.TryDequeue(out item!))
        {
            criticalBurst = 1;
            normalBurst = 0;
            return true;
        }
        if (_normal.TryDequeue(out item!))
        {
            criticalBurst = 0;
            normalBurst = 1;
            return true;
        }
        item = null!;
        return false;
    }

    private ConcurrentQueue<IEditorWorkItem> Queue(EditorTaskPriority priority) => priority switch
    {
        EditorTaskPriority.Critical => _critical,
        EditorTaskPriority.Normal => _normal,
        EditorTaskPriority.Background => _background,
        _ => throw new ArgumentOutOfRangeException(nameof(priority))
    };

    private void TryPostFailure(string name, Exception exception)
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _mainThread.Enqueue(new EditorMainThreadWorkItem(name,
                () => EditorCallbacks.Report($"Background task {name}", exception)));
        }
    }

    private void CancelQueued(ConcurrentQueue<IEditorWorkItem> queue)
    {
        while (queue.TryDequeue(out var item))
        {
            Interlocked.Decrement(ref _pendingBackground);
            item.Cancel(_shutdown.Token);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(
        Volatile.Read(ref _disposed) != 0, this);
}
