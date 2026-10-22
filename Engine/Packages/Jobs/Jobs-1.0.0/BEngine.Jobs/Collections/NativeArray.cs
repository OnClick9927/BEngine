using System.Runtime.InteropServices;

namespace BEngine.Jobs;

public enum NativeArrayOptions { UninitializedMemory, ClearMemory }

public unsafe struct NativeArray<T> : IDisposable, INativeContainer where T : unmanaged
{
    private NativeArrayState<T>? _state;

    public NativeArray(int length, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        _state = new NativeArrayState<T>(length, options == NativeArrayOptions.ClearMemory);
    }

    public readonly bool isCreated => _state is { IsDisposed: false };
    public readonly int Length => State.Length;

    public T this[int index]
    {
        readonly get => State.Read(index);
        set => State.Write(index, value);
    }

    public readonly T[] ToArray()
    {
        var state = State;
        state.CheckRead();
        var result = new T[state.Length];
        state.CopyTo(result);
        return result;
    }

    public readonly void CopyFrom(ReadOnlySpan<T> values)
    {
        var state = State;
        state.CheckWrite();
        if (values.Length != state.Length) throw new ArgumentException("Source length must match the NativeArray.", nameof(values));
        state.CopyFrom(values);
    }

    public void Dispose()
    {
        _state?.DisposeChecked();
        _state = null;
    }

    readonly INativeContainerState? INativeContainer.ContainerState => _state;
    internal readonly NativeArrayState<T> State => _state ?? throw new ObjectDisposedException(nameof(NativeArray<T>));
}

internal unsafe sealed class NativeArrayState<T> : INativeContainerState where T : unmanaged
{
    private readonly object _gate = new();
    private readonly Dictionary<long, JobAccess> _reservations = [];
    private T* _memory;
    internal int Length { get; }
    internal bool IsDisposed { get; private set; }

    internal NativeArrayState(int length, bool clear)
    {
        Length = length;
        if (length == 0) return;
        var bytes = checked((nuint)length * (nuint)sizeof(T));
        _memory = clear ? (T*)NativeMemory.AllocZeroed(bytes) : (T*)NativeMemory.Alloc(bytes);
        if (_memory is null) throw new OutOfMemoryException();
    }

    ~NativeArrayState() => DisposeUnchecked();

    internal T Read(int index)
    {
        CheckIndex(index);
        CheckRead();
        return _memory[index];
    }

    internal void Write(int index, T value)
    {
        CheckIndex(index);
        CheckWrite();
        _memory[index] = value;
    }

    internal void CopyTo(Span<T> destination)
    {
        if (Length > 0) new ReadOnlySpan<T>(_memory, Length).CopyTo(destination);
    }

    internal void CopyFrom(ReadOnlySpan<T> source)
    {
        if (Length > 0) source.CopyTo(new Span<T>(_memory, Length));
    }

    public void Reserve(long jobId, JobAccess access, IReadOnlySet<long> dependencies)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            foreach (var reservation in _reservations)
            {
                if (reservation.Key == jobId || !Conflicts(reservation.Value, access)) continue;
                if (!dependencies.Contains(reservation.Key))
                    throw new InvalidOperationException(
                        "Native container access conflicts with a scheduled job. Pass that JobHandle as a dependency.");
            }
            _reservations[jobId] = _reservations.GetValueOrDefault(jobId) | access;
        }
    }

    public void Release(long jobId)
    {
        lock (_gate) _reservations.Remove(jobId);
    }

    public void CheckRead() => CheckAccess(JobAccess.Read);
    public void CheckWrite() => CheckAccess(JobAccess.Write);

    public void DisposeChecked()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_reservations.Count > 0)
                throw new InvalidOperationException("Cannot dispose a NativeArray while a scheduled job is using it.");
            DisposeUnchecked();
        }
        GC.SuppressFinalize(this);
    }

    private void CheckAccess(JobAccess requested)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var jobId = JobExecutionContext.CurrentJobId;
            if (jobId != 0 && _reservations.TryGetValue(jobId, out var access))
            {
                if ((access & requested) == 0)
                    throw new InvalidOperationException(requested == JobAccess.Read
                        ? "A WriteOnly job field cannot be read."
                        : "A ReadOnly job field cannot be written.");
                return;
            }
            if (_reservations.Count > 0)
                throw new InvalidOperationException("Complete scheduled jobs before accessing this NativeArray.");
        }
    }

    private static bool Conflicts(JobAccess left, JobAccess right) =>
        (left & JobAccess.Write) != 0 || (right & JobAccess.Write) != 0;

    private void CheckIndex(int index)
    {
        if ((uint)index >= (uint)Length) throw new IndexOutOfRangeException();
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(NativeArray<T>));
    }

    private void DisposeUnchecked()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        if (_memory is not null) NativeMemory.Free(_memory);
        _memory = null;
    }
}
