using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BEngine;

// Snapshot only keys and a permutation, never payloads/materials/package objects.
internal sealed class RenderSubmissionSortCache2D : IComparer<int>
{
    private RenderSortKey2D[] _keys = [];
    private int[] _order = [], _work = [];
    private int _count = -1;
    private int _budget = 4 * 1024 * 1024;
    internal int BudgetBytes { get => _budget; set { ArgumentOutOfRangeException.ThrowIfNegative(value); _budget = value; Clear(); } }
    internal long RetainedBytes => (long)_keys.Length * (Unsafe.SizeOf<RenderSortKey2D>() + 2 * sizeof(int));
    internal void Clear() { _keys = []; _order = []; _work = []; _count = -1; }

    // True means a linear verification/permutation pass sufficed; false means a full stable sort.
    internal bool Sort(List<RenderSubmission2D> submissions)
    {
        var values = CollectionsMarshal.AsSpan(submissions);
        var ordered = true;
        for (var i = 1; i < values.Length; i++)
            if (values[i - 1].SortKey.CompareTo(values[i].SortKey) > 0) { ordered = false; break; }
        if (ordered) return true;
        var limit = _budget / (Unsafe.SizeOf<RenderSortKey2D>() + 2 * sizeof(int));
        if (values.Length > limit) { SortStable(submissions); _count = -1; return false; }
        if (_keys.Length < values.Length)
        {
            var capacity = Math.Min(limit, Math.Max(values.Length, Math.Max(16, _keys.Length * 2)));
            _keys = new RenderSortKey2D[capacity]; _order = new int[capacity]; _work = new int[capacity]; _count = -1;
        }
        var reused = _count == values.Length;
        if (reused)
            for (var i = 0; i < values.Length; i++)
                if (_keys[i] != values[i].SortKey) { reused = false; break; }
        if (!reused)
        {
            _count = -1;
            for (var i = 0; i < values.Length; i++) { _keys[i] = values[i].SortKey; _order[i] = i; }
            Array.Sort(_order, 0, values.Length, this);
            _count = values.Length;
        }
        _order.AsSpan(0, values.Length).CopyTo(_work);
        // Apply destination->source permutation in cycles without a second payload buffer.
        for (var start = 0; start < values.Length; start++)
        {
            if (_work[start] < 0) continue;
            if (_work[start] == start) { _work[start] = -1; continue; }
            var saved = values[start]; var current = start;
            while (true)
            {
                var next = _work[current]; _work[current] = -1;
                if (next == start) { values[current] = saved; break; }
                values[current] = values[next]; current = next;
            }
        }
        return reused;
    }
    public int Compare(int left, int right)
    {
        var key = _keys[left].CompareTo(_keys[right]);
        return key != 0 ? key : left.CompareTo(right);
    }

    internal static void SortStable(List<RenderSubmission2D> submissions)
    {
        var values = CollectionsMarshal.AsSpan(submissions);
        if (values.Length < 2) return;
        var buffer = ArrayPool<RenderSubmission2D>.Shared.Rent(values.Length);
        try
        {
            // Bottom-up merge sort gives the public Build stable ordering for equal keys too.
            for (var width = 1; width < values.Length; width = width > values.Length / 2 ? values.Length : width * 2)
            {
                for (var start = 0; start < values.Length;)
                {
                    var middle = start + Math.Min(width, values.Length - start);
                    var end = middle + Math.Min(width, values.Length - middle);
                    var left = start; var right = middle;
                    for (var output = start; output < end; output++)
                        buffer[output] = right >= end || left < middle && values[left].SortKey.CompareTo(values[right].SortKey) <= 0
                            ? values[left++] : values[right++];
                    start = end;
                }
                buffer.AsSpan(0, values.Length).CopyTo(values);
            }
        }
        finally { ArrayPool<RenderSubmission2D>.Shared.Return(buffer, clearArray: true); }
    }
}
