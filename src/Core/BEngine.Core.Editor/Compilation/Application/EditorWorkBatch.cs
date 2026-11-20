using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace BEngine.Editor;

/// <summary>Runs bounded, independent editor preparation work while pumping the caller's host.</summary>
/// <remarks>Workers must not initialize executable extensions or mutate editor state.
/// Progress and the execution pump run on the calling thread. All workers finish before returning,
/// including when an observer or worker fails. Results retain their input-index order.</remarks>
public static class EditorWorkBatch
{
    public static TResult[] Run<TResult>(int count, Func<int, TResult> build,
        Action<int, int>? progress, CancellationToken cancellationToken = default,
        int? maximumConcurrency = null)
    {
        var results = new TResult[count];
        if (count == 0) return results;
        // Metadata/reference preparation often has no observer. It needs the
        // results and completion pump, but no per-item progress queue at all.
        var updates = progress is null ? null : new ConcurrentQueue<int>();
        var completed = 0;
        var limit = maximumConcurrency ?? Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var work = Task.Run(() => Parallel.For(0, count, new ParallelOptions
        {
            MaxDegreeOfParallelism = limit,
            CancellationToken = cancellationToken
        }, index =>
        {
            updates?.Enqueue(index);
            results[index] = build(index);
            if (updates is not null) Interlocked.Increment(ref completed);
        }), cancellationToken);
        Task[] completion = [work];
        try
        {
            while (!work.IsCompleted)
            {
                if (updates is not null)
                    while (updates.TryDequeue(out var index)) progress!.Invoke(Volatile.Read(ref completed), index);
                BEngine.Editor.EditorExecutionPump.Pump();
                // Wake as soon as this dependency wave completes; sleeping for
                // a full polling interval penalizes small/cached waves.
                Task.WaitAny(completion, 15);
            }
        }
        finally
        {
            // A failed observer or one failed build cannot leave sibling builds
            // using a project/service scope after this call returns.
            try { work.GetAwaiter().GetResult(); }
            catch (AggregateException error) when (error.Flatten().InnerExceptions.Count == 1)
            { ExceptionDispatchInfo.Capture(error.Flatten().InnerExceptions[0]).Throw(); }
        }
        if (updates is not null)
            while (updates.TryDequeue(out var index)) progress!.Invoke(completed, index);
        return results;
    }
}
