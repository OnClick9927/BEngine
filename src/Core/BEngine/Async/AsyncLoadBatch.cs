using System.Runtime.ExceptionServices;

namespace BEngine;

/// <summary>
/// Bounded asynchronous preparation on the owning engine thread. Results keep input order,
/// regardless of completion order. Disposal releases only this operation's ownership.
/// </summary>
public class AsyncLoadBatch<T> : AsyncOperation, IDisposable where T : class
{
    private readonly T?[] _results;
    private readonly float[] _fractions;
    private readonly Action<T>? _release;
    private readonly Func<int, AsyncLoadContext, BValueTask<T>> _loader;
    private BCancellationRegistration _externalCancellation;
    private Exception? _failure;
    private double _progressSum;
    private int _next;
    private bool _disposed;

    public int count => _results.Length;
    public int completedCount { get; private set; }
    public int activeCount { get; private set; }

    public AsyncLoadBatch(int count, Func<int, AsyncLoadContext, BValueTask<T>> loader,
        int maximumConcurrency = 4, Action<T>? release = null, BCancellationToken cancellationToken = default)
    {
        if (count is < 0 or > 65536) throw new ArgumentOutOfRangeException(nameof(count));
        if (maximumConcurrency is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        ArgumentNullException.ThrowIfNull(loader);
        _results = new T?[count]; _fractions = new float[count];
        _loader = loader; _release = release;
        _externalCancellation = cancellationToken.Register(() => Cancel());
        Start(token => RunAsync(Math.Min(count, maximumConcurrency), token));
    }

    public float GetItemProgress(int index) => _fractions[index];

    public bool TryGetResult(int index, out T? result)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        result = _results[index];
        return result is not null;
    }

    public T GetResult(int index)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _results[index] ?? throw new InvalidOperationException("The batch item has not completed.");
    }

    private async BTask RunAsync(int workers, BCancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            var pending = new BTask[workers];
            for (var i = 0; i < pending.Length; i++) pending[i] = WorkerAsync(token);
            // Never finish failure/cancellation while a sibling loader still owns input or a lease.
            await BTask.WhenAll(pending);
            if (_failure is not null) ExceptionDispatchInfo.Capture(_failure).Throw();
            token.ThrowIfCancellationRequested();
            if (_disposed) ReleaseResults();
        }
        catch (Exception failure)
        {
            try { ReleaseResults(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("Batch preparation and result release both failed.", failure, cleanupFailure);
            }
            throw;
        }
        finally
        {
            _externalCancellation.Dispose();
        }
    }

    private async BTask WorkerAsync(BCancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && _next < count)
            {
                var index = _next++;
                activeCount++;
                try
                {
                    var context = new AsyncLoadContext(token, fraction =>
                    {
                        Report(index, fraction);
                    }) { priority = priority };
                    var result = await _loader(index, context) ??
                        throw new InvalidDataException($"Batch item {index} returned no result.");
                    _results[index] = result;
                    token.ThrowIfCancellationRequested();
                    Report(index, 1);
                    completedCount++;
                }
                finally { activeCount--; }
            }
        }
        catch (BOperationCanceledException) { Cancel(); }
        catch (Exception error) { _failure ??= error; Cancel(); }
    }

    private void Report(int index, float fraction)
    {
        var previous = _fractions[index];
        _fractions[index] = Math.Max(previous, fraction);
        _progressSum += _fractions[index] - previous;
        SetProgress((float)(_progressSum / count));
    }

    private void ReleaseResults()
    {
        List<Exception>? failures = null;
        for (var i = 0; i < _results.Length; i++)
        {
            var result = _results[i]; _results[i] = null;
            if (result is null || _release is null) continue;
            try { _release(result); }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is not null) throw new AggregateException("Batch result release failed.", failures);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        if (isDone) ReleaseResults();
    }
}
