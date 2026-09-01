namespace BEngine;

public static class RenderBatchBuilder2D
{
    public static IReadOnlyList<RenderBatch2D> Build(IEnumerable<RenderSubmission2D> submissions)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        var ordered = submissions.OrderBy(static submission => submission.SortKey).ToArray();
        return BuildOrdered(ordered);
    }

    internal static int BuildInPlace(
        List<RenderSubmission2D> submissions,
        List<RenderBatch2D> batches)
    {
        ArgumentNullException.ThrowIfNull(submissions);
        ArgumentNullException.ThrowIfNull(batches);
        submissions.Sort(static (left, right) => left.SortKey.CompareTo(right.SortKey));
        var batchCount = 0;
        for (var index = 0; index < submissions.Count; index++)
        {
            var submission = submissions[index];
            if (batchCount == 0 || batches[batchCount - 1].BatchKey != submission.BatchKey)
            {
                if (batchCount < batches.Count) batches[batchCount].Reset(submission);
                else batches.Add(new RenderBatch2D(submission));
                batchCount++;
            }
            else
                batches[batchCount - 1].Add(submission);
        }
        return batchCount;
    }

    private static IReadOnlyList<RenderBatch2D> BuildOrdered(IReadOnlyList<RenderSubmission2D> ordered)
    {
        var batches = new List<RenderBatch2D>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var submission = ordered[index];
            if (batches.Count == 0 || batches[^1].BatchKey != submission.BatchKey)
                batches.Add(new RenderBatch2D(submission));
            else
                batches[^1].Add(submission);
        }
        return batches;
    }
}
