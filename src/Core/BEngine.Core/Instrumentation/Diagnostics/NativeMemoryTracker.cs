namespace BEngine.Instrumentation;

public sealed record NativeAllocation(long Id, string Category, string Label, long Bytes, DateTimeOffset CreatedAtUtc);

/// <summary>Optional detailed recorder. Core does not retain native allocation records.</summary>
public interface INativeMemoryRecorder
{
    long Register(string category, string label, long bytes);
    void Resize(long id, long bytes);
    void Release(long id);
    NativeAllocation[] Snapshot();
}

/// <summary>Native allocation hooks. Requested sizes exclude undocumented driver overhead.</summary>
public static class NativeMemoryTracker
{
    private static readonly object Gate = new();
    private static RecorderRegistration? _active;

    public static IDisposable RegisterRecorder(INativeMemoryRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        lock (Gate)
        {
            var registration = new RecorderRegistration(recorder, _active);
            Volatile.Write(ref _active, registration);
            return registration;
        }
    }
    public static NativeAllocation[] Snapshot() => Volatile.Read(ref _active)?.Recorder?.Snapshot() ?? [];
    public static Allocation Register(string category, string label, long bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        var registration = Volatile.Read(ref _active);
        var recorder = registration?.Recorder;
        return recorder is null ? Allocation.Empty : new Allocation(registration!, recorder.Register(category, label, bytes));
    }

    internal sealed class RecorderRegistration(INativeMemoryRecorder recorder, RecorderRegistration? previous) : IDisposable
    {
        internal INativeMemoryRecorder? Recorder = recorder;
        private readonly RecorderRegistration? _previous = previous;
        public void Dispose()
        {
            lock (Gate)
            {
                Volatile.Write(ref Recorder, null);
                var active = _active;
                while (active is not null && active.Recorder is null) active = active._previous;
                Volatile.Write(ref _active, active);
            }
        }
    }
    public sealed class Allocation : IDisposable
    {
        internal static readonly Allocation Empty = new();
        private RecorderRegistration? _registration;
        private long _id;
        private Allocation() { }
        internal Allocation(RecorderRegistration registration, long id) { _registration = registration; _id = id; }
        public void Resize(long bytes)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            Volatile.Read(ref _registration)?.Recorder?.Resize(Interlocked.Read(ref _id), bytes);
        }
        public void Dispose()
        {
            var registration = Interlocked.Exchange(ref _registration, null);
            registration?.Recorder?.Release(Interlocked.Exchange(ref _id, 0));
        }
    }
}
