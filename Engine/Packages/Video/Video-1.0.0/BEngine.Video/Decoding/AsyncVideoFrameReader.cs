namespace BEngine.Video;

public sealed record VideoFrameSnapshot(int Width, int Height, double Timestamp, byte[] Rgba, long RequestId);
public sealed record VideoReaderMetadata(int Width, int Height, double FrameRate, double Duration, bool CanSeek, string Backend);

/// <summary>Coalesces decode requests on the owning BTask frame loop with one pending and one completed frame.</summary>
public sealed class AsyncVideoFrameReader : IDisposable
{
    private readonly Func<VideoFrameReader> _factory;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly TaskCompletionSource<VideoReaderMetadata> _prepared = new();
    private VideoFrameReader? _reader;
    private (long Id, double Seconds)? _pending;
    private VideoFrameSnapshot? _completed;
    private bool _disposed, _scheduled;
    private IDisposable? _work;
    private long _requestId;
    public Task<VideoReaderMetadata> Prepared => _prepared.Task;
    public long HardwareDecodedFrames { get; private set; }
    public long DecodedFrames { get; private set; }
    public Exception? Error { get; private set; }
    public bool HasPendingWork => !_disposed && (!_prepared.Task.IsCompleted || _scheduled || _pending is not null || _completed is not null);

    public AsyncVideoFrameReader(Func<VideoFrameReader> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        Schedule();
    }

    public long Request(double seconds)
    {
        CheckOwner(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Error is not null) throw new InvalidOperationException("The video decoder has failed.", Error);
        var id = ++_requestId; _pending = (id, seconds); _completed = null; Schedule(); return id;
    }

    public bool TryTakeFrame(out VideoFrameSnapshot frame)
    {
        CheckOwner(); frame = _completed!; _completed = null; return frame is not null;
    }

    private void Schedule()
    {
        if (_scheduled || _disposed) return;
        _scheduled = true;
        try { _work = CooperativeWorkScheduler.Main.Schedule(() =>
        {
            Decode();
            if (!_disposed && Error is null && _pending is not null) return CooperativeWorkStatus.Continue;
            _work = null; _scheduled = false; return CooperativeWorkStatus.Complete;
        }, Fail, CooperativeWorkPriority.Background); }
        catch { _scheduled = false; throw; }
    }

    private void Decode()
    {
        CheckOwner();
        if (_disposed) return;
        try
        {
            if (_reader is null)
            {
                var created = _factory();
                if (_disposed) { created.Dispose(); return; }
                _reader = created;
                _prepared.TrySetResult(new(_reader.width, _reader.height, _reader.frameRate, _reader.duration, _reader.canSeek, _reader.backend));
            }
            if (_disposed || _reader is null) return;
            if (_pending is not { } request) return;
            _pending = null;
            var pixels = new byte[checked(_reader.width * _reader.height * 4)];
            _reader.ReadFrame(Math.Min(request.Seconds, _reader.duration), pixels);
            if (_disposed || _reader is null) return;
            HardwareDecodedFrames = _reader.hardwareDecodedFrames; DecodedFrames = _reader.decodedFrames;
            if (!_disposed && request.Id == _requestId)
                _completed = new(_reader.width, _reader.height, _reader.timestamp, pixels, request.Id);
        }
        catch (Exception error)
        {
            Fail(error);
        }
    }
    private void Fail(Exception error)
    {
        _scheduled = false; _pending = null; _completed = null; Error = error; _prepared.TrySetException(error);
        var reader = _reader; _reader = null;
        try { reader?.Dispose(); } finally { _work?.Dispose(); _work = null; }
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Video requests and decode callbacks belong to the engine thread.");
    }

    public void Dispose()
    {
        CheckOwner();
        if (_disposed) return;
        _disposed = true; _pending = null; _completed = null; _prepared.TrySetCanceled();
        _work?.Dispose(); _work = null; _scheduled = false;
        _reader?.Dispose(); _reader = null; GC.SuppressFinalize(this);
    }

    public bool WaitForShutdown(TimeSpan timeout) => _disposed;
}
