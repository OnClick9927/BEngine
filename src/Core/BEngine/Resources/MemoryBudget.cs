namespace BEngine;

/// <summary>Single-thread admission budget. Leases remain charged until the underlying resource is released.</summary>
public sealed class MemoryBudget
{
    private long _limit;
    public MemoryBudget(long limitBytes) => LimitBytes = limitBytes;
    public long LimitBytes
    {
        get => _limit;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_limit == value) return;
            _limit = value; LimitChanged?.Invoke(this);
        }
    }
    public event Action<MemoryBudget>? LimitChanged;
    public long ReservedBytes { get; private set; }
    public long AvailableBytes => Math.Max(0, _limit - ReservedBytes);
    public long RejectedRequests { get; private set; }
    public bool TryReserve(long bytes, out Lease? lease)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes > AvailableBytes) { RejectedRequests++; lease = null; return false; }
        ReservedBytes += bytes; lease = new Lease(this, bytes); return true;
    }
    public sealed class Lease : IDisposable
    {
        private MemoryBudget? _owner;
        public long Bytes { get; }
        internal Lease(MemoryBudget owner, long bytes) { _owner = owner; Bytes = bytes; }
        public void Dispose()
        {
            if (_owner is not { } owner) return;
            _owner = null; owner.ReservedBytes -= Bytes;
        }
    }
}
