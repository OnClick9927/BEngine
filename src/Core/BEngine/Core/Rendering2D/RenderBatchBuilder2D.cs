namespace BEngine;

public static class RenderBatchBuilder2D
{
    public static IReadOnlyList<RenderBatch2D> Build(IEnumerable<RenderSubmission2D> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        var ordered = submissions.OrderBy(item => item.SortKey).ToArray();
        var batches = new List<RenderBatch2D>();
        foreach (var submission in ordered)
        {
            if (batches.Count == 0 || batches[^1].BatchKey != submission.BatchKey)
                batches.Add(new RenderBatch2D(submission));
            else
                batches[^1].Add(submission);
        }
        return batches;
    }
}
