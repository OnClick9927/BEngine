namespace BEngine;

public sealed class RenderBatch2D
{
    private readonly List<RenderSubmission2D> _submissions = [];

    internal RenderBatch2D(RenderSubmission2D first)
    {
        Reset(first);
    }

    public RenderBatchKey2D BatchKey { get; private set; }
    public IReadOnlyList<RenderSubmission2D> Submissions => _submissions;
    internal void Reset(RenderSubmission2D first)
    {
        BatchKey = first.BatchKey;
        _submissions.Clear();
        _submissions.Add(first);
    }
    internal void Add(RenderSubmission2D submission) => _submissions.Add(submission);
    internal void Clear() => _submissions.Clear();
}
