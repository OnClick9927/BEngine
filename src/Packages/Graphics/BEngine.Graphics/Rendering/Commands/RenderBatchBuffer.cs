using System.Collections;
using System.Runtime.InteropServices;

namespace BEngine;

/// <summary>
/// Reuses batches over contiguous, equal-key ranges of an already ordered command list.
/// The producer chooses the order; this buffer neither sorts commands nor copies payloads.
/// </summary>
public sealed class RenderBatchBuffer : IReadOnlyList<RenderBatch>, IDisposable
{
    private readonly List<RenderBatch> _batches = [];
    private int _count;
    private bool _disposed;

    public int Count => _count;
    public RenderBatch this[int index] => (uint)index < (uint)_count
        ? _batches[index] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>
    /// Borrows the source until Clear, Dispose, or the next build. Do not mutate the list,
    /// retain batches across builds, or render concurrently through the same buffer.
    /// Adjacent commands merge only when their complete RenderBatchKey values match.
    /// </summary>
    public int BuildOrdered(List<RenderSubmission> submissions)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(submissions);
        Clear();
        var values = CollectionsMarshal.AsSpan(submissions);
        try
        {
            for (var index = 0; index < values.Length;)
            {
                var start = index++;
                var key = values[start].BatchKey;
                while (index < values.Length && values[index].BatchKey == key) index++;
                if (_count == _batches.Count) _batches.Add(new RenderBatch());
                _batches[_count].Reset(submissions, start, index - start);
                _count++;
            }
            return _count;
        }
        catch
        {
            Clear();
            throw;
        }
    }

    /// <summary>Releases borrowed commands and batch keys, retaining empty batch objects for reuse.</summary>
    public void Clear()
    {
        for (var index = 0; index < _count; index++) _batches[index].Clear();
        _count = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear();
        _batches.Clear();
        _batches.TrimExcess();
        _disposed = true;
    }

    public IEnumerator<RenderBatch> GetEnumerator()
    {
        for (var index = 0; index < _count; index++) yield return _batches[index];
    }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
