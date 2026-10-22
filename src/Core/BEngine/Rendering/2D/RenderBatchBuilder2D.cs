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
        RenderSubmissionSortCache2D.SortStable(submissions);
        return BuildContiguous(submissions, batches);
    }

    internal static int BuildContiguous(List<RenderSubmission2D> submissions, List<RenderBatch2D> batches)
    {
        var batchCount = 0;
        try
        {
            for (var index = 0; index < submissions.Count;)
            {
                var start = index++;
                var key = submissions[start].BatchKey;
                while (index < submissions.Count && submissions[index].BatchKey == key) index++;
                if (batchCount == batches.Count) batches.Add(new RenderBatch2D());
                batches[batchCount++].Reset(submissions, start, index - start);
            }
            return batchCount;
        }
        catch
        {
            for (var index = 0; index < batchCount; index++) batches[index].Clear();
            throw;
        }
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
