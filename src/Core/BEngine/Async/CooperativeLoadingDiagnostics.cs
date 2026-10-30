using System.Runtime.CompilerServices;

namespace BEngine;

// QA-only prototype. Disabled branches allocate no state, inspect no delegates
// and read no clocks. Snapshots are explicit pulls, never progress events.
internal static class CooperativeLoadingDiagnostics
{
    private const int MaximumSamples=256;
    private static ConditionalWeakTable<IDisposable,Operation>? _operations;
    private static Dictionary<string,Aggregate>? _aggregates;
    private static List<LoadingWorkSample>? _samples;
    private static long _scheduled,_inlineAdmitted,_inlineRejected,_completed,_failed;
    internal static bool Enabled{get;private set;}
    internal static void SetEnabled(bool value)
    {
        Enabled=value;
        _operations=value?new():null;_aggregates=value?new(StringComparer.Ordinal):null;_samples=value?new(MaximumSamples):null;
        _scheduled=_inlineAdmitted=_inlineRejected=_completed=_failed=0;
    }
    internal static void Scheduled(IDisposable handle,Delegate step,long frame,int pending)
    {
        if(!Enabled)return;
        var method=step.Method;var label=method.DeclaringType?.FullName+"."+method.Name;
        var operation=new Operation(label,frame,pending);
        _operations!.Add(handle,operation);_scheduled++;Get(label).Scheduled++;
    }
    internal static void Disposed(IDisposable handle,long frame)
    {
        if(!Enabled||!_operations!.TryGetValue(handle,out var operation))return;
        operation.Disposed=true;operation.DisposedFrame=frame;
        // In-step normal completion disposes before Step returns. Let End record
        // that outcome; disposal outside a running step is an abandoned request.
        if(operation.InStep)return;
        End(handle,frame,success:false);
    }
    internal static void Admission(IDisposable handle,string reason,long frame,int pending,double remaining)
    {
        if(!Enabled||!_operations!.TryGetValue(handle,out var operation))return;
        operation.Reason=reason;operation.AdmissionFrame=frame;operation.AdmissionPending=pending;operation.Remaining=double.IsFinite(remaining)?remaining:0;
        var aggregate=Get(operation.Label);
        if(reason=="admitted"){_inlineAdmitted++;aggregate.InlineAdmitted++;}
        else{_inlineRejected++;aggregate.InlineRejected++;aggregate.Reasons[reason]=aggregate.Reasons.GetValueOrDefault(reason)+1;}
    }
    internal static void FirstStep(IDisposable handle,long frame)
    {
        if(!Enabled||!_operations!.TryGetValue(handle,out var operation))return;
        operation.InStep=true;
        if(operation.FirstFrame>=0)return;
        operation.FirstFrame=frame;var delay=frame-operation.ScheduledFrame;var aggregate=Get(operation.Label);
        aggregate.FirstStepDelayFrames+=delay;aggregate.MaximumFirstStepDelayFrames=Math.Max(aggregate.MaximumFirstStepDelayFrames,delay);
    }
    internal static void StepReturned(IDisposable handle,long frame,CooperativeWorkStatus status)
    {
        if(!Enabled||!_operations!.TryGetValue(handle,out var operation))return;
        operation.InStep=false;
        if(status==CooperativeWorkStatus.Complete||operation.Disposed)End(handle,frame,success:status==CooperativeWorkStatus.Complete);
    }
    internal static void End(IDisposable handle,long frame,bool success)
    {
        if(!Enabled||!_operations!.TryGetValue(handle,out var operation))return;
        _operations.Remove(handle);var aggregate=Get(operation.Label);
        if(success){_completed++;aggregate.Completed++;}else{_failed++;aggregate.Failed++;}
        var elapsed=frame-operation.ScheduledFrame;
        aggregate.CompletionDelayFrames+=elapsed;aggregate.MaximumCompletionDelayFrames=Math.Max(aggregate.MaximumCompletionDelayFrames,elapsed);
        var sameFrame=operation.FirstFrame==frame;
        if(sameFrame&&operation.Reason is not ("admitted" or "none"))aggregate.CompletedOnFirstStepFrameAfterRefusal++;
        if(_samples!.Count<MaximumSamples && (elapsed>0 || operation.Reason is not ("admitted" or "none")))
            _samples.Add(new(operation.Label,operation.ScheduledFrame,operation.FirstFrame,frame,operation.Reason,
                operation.ScheduledPending,operation.AdmissionPending,operation.Remaining,success));
    }
    internal static LoadingWorkDiagnosticsSnapshot? Snapshot=>!Enabled?null:new(
        _scheduled,_inlineAdmitted,_inlineRejected,_completed,_failed,
        _aggregates!.Select(pair=>new LoadingWorkAggregate(pair.Key,pair.Value.Scheduled,pair.Value.InlineAdmitted,pair.Value.InlineRejected,
            pair.Value.Completed,pair.Value.Failed,pair.Value.FirstStepDelayFrames,pair.Value.MaximumFirstStepDelayFrames,
            pair.Value.CompletionDelayFrames,pair.Value.MaximumCompletionDelayFrames,pair.Value.CompletedOnFirstStepFrameAfterRefusal,
            new Dictionary<string,long>(pair.Value.Reasons,StringComparer.Ordinal))).ToArray(),_samples!.ToArray());
    private static Aggregate Get(string label)
    {
        if(_aggregates!.TryGetValue(label,out var value))return value;
        // Labels are strings only, never retained Type/Delegate/Assembly owners.
        if(_aggregates.Count>=64)label="other";
        if(!_aggregates.TryGetValue(label,out value))_aggregates.Add(label,value=new());
        return value;
    }
    private sealed class Operation(string label,long frame,int pending)
    {
        internal readonly string Label=label;internal readonly long ScheduledFrame=frame;internal readonly int ScheduledPending=pending;
        internal long FirstFrame=-1,AdmissionFrame=-1;internal string Reason="none";
        internal int AdmissionPending;internal double Remaining;internal bool InStep,Disposed;internal long DisposedFrame;
    }
    private sealed class Aggregate
    {
        internal long Scheduled,InlineAdmitted,InlineRejected,Completed,Failed,FirstStepDelayFrames,MaximumFirstStepDelayFrames,
            CompletionDelayFrames,MaximumCompletionDelayFrames,CompletedOnFirstStepFrameAfterRefusal;
        internal readonly Dictionary<string,long> Reasons=new(StringComparer.Ordinal);
    }
}
internal sealed record LoadingWorkDiagnosticsSnapshot(long Scheduled,long InlineAdmitted,long InlineRejected,long Completed,long Failed,LoadingWorkAggregate[] Aggregates,LoadingWorkSample[] Samples);
internal sealed record LoadingWorkAggregate(string Label,long Scheduled,long InlineAdmitted,long InlineRejected,long Completed,long Failed,long FirstStepDelayFrames,long MaximumFirstStepDelayFrames,long CompletionDelayFrames,long MaximumCompletionDelayFrames,long CompletedOnFirstStepFrameAfterRefusal,Dictionary<string,long> Reasons);
internal sealed record LoadingWorkSample(string Label,long ScheduledFrame,long FirstFrame,long CompletedFrame,string AdmissionReason,int ScheduledPending,int AdmissionPending,double RemainingMilliseconds,bool Success);
