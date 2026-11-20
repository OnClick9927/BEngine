namespace BEngine;

/// <summary>Plain snapshot contracts shared by optional recorders and host diagnostics transports.</summary>
public sealed record LoadingWorkDiagnosticsSnapshot(long Scheduled, long InlineAdmitted,
    long InlineRejected, long Completed, long Failed,
    LoadingWorkAggregate[] Aggregates, LoadingWorkSample[] Samples);

public sealed record LoadingWorkAggregate(string Label, long Scheduled, long InlineAdmitted,
    long InlineRejected, long Completed, long Failed, long FirstStepDelayFrames,
    long MaximumFirstStepDelayFrames, long CompletionDelayFrames, long MaximumCompletionDelayFrames,
    long CompletedOnFirstStepFrameAfterRefusal, Dictionary<string, long> Reasons);

public sealed record LoadingWorkSample(string Label, long ScheduledFrame, long FirstFrame,
    long CompletedFrame, string AdmissionReason, int ScheduledPending, int AdmissionPending,
    double RemainingMilliseconds, bool Success);
