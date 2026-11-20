namespace BEngine;

public static class RenderBatchBuilder2D
{
    public static IReadOnlyList<RenderBatch> Build(IEnumerable<RenderSubmission> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        var ordered = submissions.ToList();
        RenderSubmissionSortCache2D.SortStable(ordered);
        var batches = new RenderBatchBuffer();
        batches.BuildOrdered(ordered);
        return batches;
    }
}
