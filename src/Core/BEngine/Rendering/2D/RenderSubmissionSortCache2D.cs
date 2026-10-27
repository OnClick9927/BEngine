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
        var sameCount = _count == values.Length;
        var reused = sameCount;
        if (reused)
            for (var i = 0; i < values.Length; i++)
                if (_keys[i] != values[i].SortKey) { reused = false; break; }
        if (!reused)
        {
            _count = -1;
            for (var i = 0; i < values.Length; i++) _keys[i] = values[i].SortKey;
            // The old permutation can remain stable even when individual keys
            // change. Compare includes the original input index for equal keys.
            reused = sameCount && IsCurrentOrderStable(values.Length);
            if (!reused)
            {
                for (var i = 0; i < values.Length; i++) _order[i] = i;
                if (!TrySortNaturalRuns(values.Length)) Array.Sort(_order, 0, values.Length, this);
            }
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
    private bool IsCurrentOrderStable(int count)
    {
        for (var i = 1; i < count; i++)
            if (Compare(_order[i - 1], _order[i]) > 0) return false;
        return true;
    }

    // Geometry producers commonly emit a few already ordered runs. Merge only
    // those runs, using the existing index workspace. Highly fragmented input
    // keeps the bounded framework sort instead of paying for many merge passes.
    private bool TrySortNaturalRuns(int count)
    {
        if (count < 64) return false;
        Span<int> starts = stackalloc int[33];
        starts[0] = 0;
        int runs = 1;
        for (int i = 1; i < count; i++)
        {
            if (Compare(_order[i - 1], _order[i]) <= 0) continue;
            if (runs == 32) return false;
            starts[runs++] = i;
        }
        starts[runs] = count;
        int[] input = _order, output = _work;
        while (runs > 1)
        {
            int mergedRuns = 0;
            for (int run = 0; run < runs; run += 2)
            {
                int start = starts[run], middle = starts[run + 1];
                int end = run + 1 < runs ? starts[run + 2] : middle;
                starts[mergedRuns++] = start;
                int left = start, right = middle, target = start;
                while (left < middle && right < end)
                {
                    if (Compare(input[left], input[right]) <= 0) output[target++] = input[left++];
                    else output[target++] = input[right++];
                }
                input.AsSpan(left, middle - left).CopyTo(output.AsSpan(target));
                target += middle - left;
                input.AsSpan(right, end - right).CopyTo(output.AsSpan(target));
            }
            starts[mergedRuns] = count;
            runs = mergedRuns;
            (input, output) = (output, input);
        }
        if (!ReferenceEquals(input, _order)) input.AsSpan(0, count).CopyTo(_order);
        return true;
    }

    public int Compare(int left, int right)
    {
        var key = OperatingSystem.IsBrowser()
            ? RenderSortKey2D.Compare(in _keys[left], in _keys[right])
            : _keys[left].CompareTo(_keys[right]);
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
