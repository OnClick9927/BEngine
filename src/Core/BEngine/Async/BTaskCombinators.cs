namespace BEngine;

// Keep the implementation on a non-generic type. Mono AOT eagerly scans locals
// on generic types; a closure containing BTaskCompletionSource<TResult[]> there
// otherwise recursively introduces TResult[][], TResult[][][], and so on.
// BTask<TResult> retains its public forwarding methods for existing game binaries.
internal static class BTaskCombinators
{
    public static BTask<TResult[]> WhenAll<TResult>(params BTask<TResult>[] tasks) =>
        WhenAll<TResult>((IEnumerable<BTask<TResult>>)tasks);

    public static BTask<TResult[]> WhenAll<TResult>(IEnumerable<BTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var values = tasks.ToArray();
        if (values.Any(static task => task is null))
            throw new ArgumentException("The task collection contains a null task.", nameof(tasks));
        if (values.Length == 0) return BTask<TResult[]>.FromResult([]);

        var source = new BTaskCompletionSource<TResult[]>();
        var results = new TResult[values.Length];
        var remaining = values.Length;
        List<Exception>? failures = null;
        var canceled = false;
        var cancellationToken = default(BCancellationToken);
        for (var index = 0; index < values.Length; index++)
        {
            var capturedIndex = index;
            var task = values[index];
            if (task.IsCompleted) Complete(task, capturedIndex);
            else task.RegisterContinuation(() => Complete(task, capturedIndex));
        }
        return source.Task;

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

    public static BTask<BTask<TResult>> WhenAny<TResult>(params BTask<TResult>[] tasks) =>
        WhenAny<TResult>((IEnumerable<BTask<TResult>>)tasks);

    public static BTask<BTask<TResult>> WhenAny<TResult>(IEnumerable<BTask<TResult>> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        var values = tasks.ToArray();
        if (values.Any(static task => task is null))
            throw new ArgumentException("The task collection contains a null task.", nameof(tasks));
        if (values.Length == 0)
            throw new ArgumentException("At least one task is required.", nameof(tasks));
        var completed = values.FirstOrDefault(static task => task.IsCompleted);
        if (completed is not null) return BTask<BTask<TResult>>.FromResult(completed);
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

}
