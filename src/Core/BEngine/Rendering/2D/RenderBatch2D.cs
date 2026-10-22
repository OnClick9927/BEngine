using System.Runtime.InteropServices;

namespace BEngine;

public sealed class RenderBatch2D
{
    private readonly List<RenderSubmission2D>? _owned;
    private List<RenderSubmission2D>? _source;
    private int _offset, _count;
    private readonly SubmissionView _view;

    internal RenderBatch2D() => _view = new(this);

    internal RenderBatch2D(RenderSubmission2D first)
    {
        _view = new(this);
        _owned = [];
        Reset(first);
    }

    public RenderBatchKey2D BatchKey { get; private set; }
    public IReadOnlyList<RenderSubmission2D> Submissions => _view;
    /// <summary>Valid until the batch is reset. Do not retain across render callbacks.</summary>
    public ReadOnlySpan<RenderSubmission2D> SubmissionSpan => _source is { } source
        ? CollectionsMarshal.AsSpan(source).Slice(_offset, _count) : ReadOnlySpan<RenderSubmission2D>.Empty;
    /// <summary>Checks a nonempty batch without allocating an enumerator or a predicate.</summary>
    public bool HasOnlyPayloads<TPayload>()
    {
        var submissions = SubmissionSpan;
        if (submissions.IsEmpty) return false;
        foreach (var submission in submissions) if (submission.Payload is not TPayload) return false;
        return true;
    }
    internal void Reset(RenderSubmission2D first)
    {
        BatchKey = first.BatchKey;
        _owned!.Clear(); _owned.Add(first);
        _source = _owned; _offset = 0; _count = 1;
    }
    internal void Add(RenderSubmission2D submission) { _owned!.Add(submission); _count++; }
    // Frame-local batches borrow one contiguous range; no submission copies or per-batch capacity.
    internal void Reset(List<RenderSubmission2D> source, int offset, int count)
    {
        _owned?.Clear();
        BatchKey = source[offset].BatchKey;
        _source = source; _offset = offset; _count = count;
    }
    internal void Clear() { _owned?.Clear(); _source = null; _offset = _count = 0; }

    private sealed class SubmissionView(RenderBatch2D owner) : IReadOnlyList<RenderSubmission2D>
    {
        public int Count => owner._count;
        public RenderSubmission2D this[int index] => (uint)index < (uint)owner._count
            ? owner._source![owner._offset + index] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<RenderSubmission2D> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
