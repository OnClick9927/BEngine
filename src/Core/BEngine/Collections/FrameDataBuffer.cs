using System.Runtime.CompilerServices;

namespace BEngine;

/// <summary>Reusable contiguous value storage for synchronous batches on the runtime thread.
/// Spans and references are borrowed: do not retain across growth, Clear, TrimExcess or await.</summary>
public sealed class FrameDataBuffer<T> where T : struct
{
    private T[] _items;
    public int Count { get; private set; }
    public int Capacity => _items.Length;
    public FrameDataBuffer(int capacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _items = capacity == 0 ? [] : new T[capacity];
    }
    public Span<T> WrittenSpan => _items.AsSpan(0, Count);
    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return ref _items[index];
        }
    }
    public int Add(in T value)
    {
        EnsureCapacity(checked(Count + 1));
        _items[Count] = value;
        return Count++;
    }
    public void EnsureCapacity(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        if (capacity <= _items.Length) return;
        // Large records make spare slots expensive. Keep amortized constant-time append
        // while growing them by 25%; small records retain the inexpensive doubling policy.
        var requestedGrowth = Unsafe.SizeOf<T>() >= 128 && _items.Length >= 64
            ? (long)_items.Length + Math.Max(16, _items.Length / 4)
            : (long)_items.Length * 2;
        var growth = Math.Min(Array.MaxLength, Math.Max(16L, requestedGrowth));
        Array.Resize(ref _items, checked((int)Math.Max(capacity, growth)));
    }
    public void Clear()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) Array.Clear(_items, 0, Count);
        Count = 0;
    }
    /// <summary>Explicit maintenance operation; avoid calling in a per-frame loop.</summary>
    public void TrimExcess() { if (_items.Length != Count) Array.Resize(ref _items, Count); }
}
