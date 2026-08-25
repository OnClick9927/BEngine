namespace BEngine;

public sealed class RenderBatch2D
{
    private readonly List<RenderSubmission2D> _submissions = [];

    internal RenderBatch2D(RenderSubmission2D first)
    {
        BatchKey = first.BatchKey;
        _submissions.Add(first);
    }

    public RenderBatchKey2D BatchKey { get; }
    public IReadOnlyList<RenderSubmission2D> Submissions => _submissions;
    internal void Add(RenderSubmission2D submission) => _submissions.Add(submission);
}
