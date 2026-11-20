namespace BEngine;

/// <summary>Optional loading instrumentation. The default runtime has no recorder or sample buffers.</summary>
public interface ILoadingWorkDiagnostics
{
    bool Enabled { get; }
    void SetEnabled(bool enabled);
    LoadingWorkDiagnosticsSnapshot? Capture();
    void Scheduled(IDisposable handle, Delegate step, long frame, int pending);
    void Disposed(IDisposable handle, long frame);
    void Admission(IDisposable handle, string reason, long frame, int pending, double remaining);
    void FirstStep(IDisposable handle, long frame);
    void StepReturned(IDisposable handle, long frame, CooperativeWorkStatus status);
    void End(IDisposable handle, long frame, bool success);
}
