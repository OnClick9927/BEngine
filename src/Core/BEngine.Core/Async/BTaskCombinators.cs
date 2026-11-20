namespace BEngine;

// Keep the implementation on a non-generic type. Mono AOT eagerly scans locals
// on generic types; a closure containing BTaskCompletionSource<TResult[]> there
// otherwise recursively introduces TResult[][], TResult[][][], and so on.
// BTask<TResult> retains its public forwarding methods for existing game binaries.
internal static class BTaskCombinators
{
    public static BTask<TResult[]> WhenAll<TResult>(params BTask<TResult>[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        Validate(tasks);
        return WhenAllCore(tasks, ownsSnapshot: false);
    }

    public static BTask<TResult[]> WhenAll<TResult>(IEnumerable<BTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var values = tasks.ToArray();
        Validate(values);
        return WhenAllCore(values, ownsSnapshot: true);
    }

    private static BTask<TResult[]> WhenAllCore<TResult>(BTask<TResult>[] values, bool ownsSnapshot)
    {
        if (values.Length == 0) return BTask<TResult[]>.FromResult([]);

        var source = new BTaskCompletionSource<TResult[]>();
        var results = new TResult[values.Length];
        var remaining = values.Length;
        List<Exception>? failures = null;
        var canceled = false;
        var cancellationToken = default(BCancellationToken);
        // ToArray above owns its snapshot; params arrays still need copying when pending.
        var snapshot = ownsSnapshot;
        for (var index = 0; index < values.Length; index++)
        {
            var task = values[index];
            if (task.IsCompleted) Complete(task, index);
            else
            {
                if (!snapshot) { values = values.ToArray(); snapshot = true; task = values[index]; }
                RegisterPending(task, index);
            }
        }
        return source.Task;

        // Only pending tasks need a closure retaining their task and result slot.
        // Keep it out of the loop scope so completed inputs do not allocate one.
        void RegisterPending(BTask<TResult> task, int index) =>
            task.RegisterContinuation(() => Complete(task, index));

        void Complete(BTask<TResult> task, int index)
        {
            try { results[index] = task.GetResultValue(); }
            catch (BOperationCanceledException exception)
            {
                canceled = true;
                cancellationToken = exception.CancellationToken;
            }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            remaining--;
            if (remaining != 0) return;
            if (failures is { Count: > 0 })
                source.TrySetException(failures.Count == 1
                    ? failures[0]
                    : new AggregateException(failures));
            else if (canceled)
                source.TrySetCanceled(cancellationToken);
            else
                source.TrySetResult(results);
        }
    }

    public static BTask<BTask<TResult>> WhenAny<TResult>(params BTask<TResult>[] tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        Validate(tasks);
        return WhenAnyCore(tasks, nameof(tasks), ownsSnapshot: false);
    }

    public static BTask<BTask<TResult>> WhenAny<TResult>(IEnumerable<BTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var values = tasks.ToArray();
        Validate(values);
        return WhenAnyCore(values, nameof(tasks), ownsSnapshot: true);
    }

    private static BTask<BTask<TResult>> WhenAnyCore<TResult>(BTask<TResult>[] values, string parameterName, bool ownsSnapshot)
    {
        if (values.Length == 0)
            throw new ArgumentException("At least one task is required.", parameterName);
        foreach (var task in values)
            if (task.IsCompleted) return BTask<BTask<TResult>>.FromResult(task);
        if (!ownsSnapshot) values = values.ToArray();
        var source = new BTaskCompletionSource<BTask<TResult>>();
        var won = false;
        foreach (var task in values)
            task.RegisterContinuation(() =>
            {
                if (won) return;
                won = true;
                source.TrySetResult(task);
            });
        return source.Task;
    }

    private static void Validate<TResult>(BTask<TResult>[] tasks)
    {
        foreach (var task in tasks)
            if (task is null) throw new ArgumentException("The task collection contains a null task.", nameof(tasks));
    }

}
