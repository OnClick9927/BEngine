namespace BEngine;

/// <summary>Cancellation, progress and resumable main-thread work for asset/scene loaders.</summary>
public sealed class AsyncLoadContext
{
    private readonly HashSet<IDisposable> _work = [];
    private readonly Action<float>? _report;
    private int _priority;
    public BCancellationToken cancellationToken { get; }
    public int priority
    {
        get => _priority;
        set
        {
            _priority = value;
            foreach (var handle in _work) CooperativeWorkScheduler.Main.SetPriority(handle, WorkPriority);
        }
    }
    private CooperativeWorkPriority WorkPriority => _priority > 0 ? CooperativeWorkPriority.Realtime
        : _priority < 0 ? CooperativeWorkPriority.Background : CooperativeWorkPriority.Normal;
    public AsyncLoadContext(BCancellationToken cancellationToken = default, Action<float>? progress = null)
    { this.cancellationToken = cancellationToken; _report = progress; }
    public void ReportProgress(float value)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        _report?.Invoke(Math.Clamp(value, 0, 1));
    }
    /// <summary>Runs bounded steps under the shared cooperative budget. Each step must return promptly.</summary>
    public BTask RunSteps(Func<CooperativeWorkStatus> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new BTaskCompletionSource();
        IDisposable? handle = null;
        BCancellationRegistration cancellation = default;
        void Cleanup()
        {
            if (handle is not null) { _work.Remove(handle); handle.Dispose(); }
            cancellation.Dispose();
        }
        handle = CooperativeWorkScheduler.Main.Schedule(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = step();
            if (status == CooperativeWorkStatus.Complete) { Cleanup(); completion.TrySetResult(); }
            return status;
        }, error =>
        {
            Cleanup();
            if (error is BOperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
            else completion.TrySetException(error);
        }, WorkPriority);
        _work.Add(handle);
        cancellation = cancellationToken.Register(() => { Cleanup(); completion.TrySetCanceled(cancellationToken); });
        return completion.Task;
    }
    /// <summary>Reads on a bounded worker (cooperative 64 KiB steps on Web); the caller retains the stream until completion.</summary>
    public async BTask<byte[]> ReadAllBytesAsync(Stream stream, int maximumBytes = 256 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(stream); ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        if (!stream.CanRead || !stream.CanSeek) throw new NotSupportedException("Cooperative byte reads require a readable, seekable stream.");
        var length = stream.Length - stream.Position;
        if (length < 0 || length > maximumBytes) throw new InvalidDataException("The resource exceeds the cooperative read size limit.");
        if (BackgroundWorkScheduler.IsSupported)
        {
            // The caller retains the stream until this task completes. No
            // worker ever reports progress or completes an engine BTask.
            var loaded = await BackgroundWorkScheduler.Main.Run(token =>
            {
                var result = new byte[(int)length];
                var position = 0;
                while (position < result.Length)
                {
                    token.ThrowIfCancellationRequested();
                    var count = Math.Min(64 * 1024, result.Length - position);
                    var read = stream.Read(result, position, count);
                    if (read == 0) throw new EndOfStreamException();
                    position += read;
                }
                return result;
            }, cancellationToken);
            ReportProgress(.85f);
            return loaded;
        }
        byte[]? bytes = null; var offset = 0;
        await RunSteps(() =>
        {
            if (bytes is null) { bytes = new byte[(int)length]; return length == 0 ? CooperativeWorkStatus.Complete : CooperativeWorkStatus.Continue; }
            var count = Math.Min(64 * 1024, bytes.Length - offset);
            stream.ReadExactly(bytes.AsSpan(offset, count)); offset += count;
            ReportProgress(.05f + .8f * offset / bytes.Length);
            return offset == bytes.Length ? CooperativeWorkStatus.Complete : CooperativeWorkStatus.Continue;
        });
        return bytes!;
    }
}
