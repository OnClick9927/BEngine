using System.Runtime.InteropServices;

namespace BEngine;

/// <summary>A borrowed command range, valid only until its owning buffer is cleared or rebuilt.</summary>
[RuntimeTypeAlias("BEngine.RenderBatch2D")]
[RuntimeTypeAlias("BEngine.RenderBatch2D, BEngine.Graphics")]
[RuntimeTypeAlias("BEngine.RenderBatch2D, BEngine.Graphics, Version=0.1.0.0, Culture=neutral, PublicKeyToken=null")]
public sealed class RenderBatch
{
    private List<RenderSubmission>? _source;
    private int _offset, _count;
    private readonly SubmissionView _view;

    internal RenderBatch() => _view = new(this);

    public RenderBatchKey BatchKey { get; private set; }
    public IReadOnlyList<RenderSubmission> Submissions => _view;
    /// <summary>Valid until the batch is reset. Do not retain across render callbacks.</summary>
    public ReadOnlySpan<RenderSubmission> SubmissionSpan => _source is { } source
        ? CollectionsMarshal.AsSpan(source).Slice(_offset, _count) : ReadOnlySpan<RenderSubmission>.Empty;
    /// <summary>Checks a nonempty batch without allocating an enumerator or a predicate.</summary>
    public bool HasOnlyPayloads<TPayload>()
    {
        var submissions = SubmissionSpan;
        if (submissions.IsEmpty) return false;
        foreach (ref readonly var submission in submissions) if (submission.Payload is not TPayload) return false;
        return true;
    }
    // Frame-local batches borrow one contiguous range; no submission copies or per-batch capacity.
    internal void Reset(List<RenderSubmission> source, int offset, int count)
    {
        BatchKey = source[offset].BatchKey;
        _source = source; _offset = offset; _count = count;
    }
    internal void Clear() { BatchKey = default; _source = null; _offset = _count = 0; }

    private sealed class SubmissionView(RenderBatch owner) : IReadOnlyList<RenderSubmission>
    {
        public int Count => owner._count;
        public RenderSubmission this[int index] => (uint)index < (uint)owner._count
            ? owner._source![owner._offset + index] : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<RenderSubmission> GetEnumerator()
        {
            for (var index = 0; index < Count; index++) yield return this[index];
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
