using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;

namespace BEngine;

/// <summary>Explicitly enabled pull-based loading measurements.</summary>
public sealed class LoadingWorkDiagnostics : ILoadingWorkDiagnostics
{
    [ModuleInitializer]
    internal static void Register() => RuntimeServiceContributions.RegisterDefault<ILoadingWorkDiagnostics>(new LoadingWorkDiagnostics());
    public bool Enabled => LoadingWorkRecorder.Enabled;
    public void SetEnabled(bool enabled) => LoadingWorkRecorder.SetEnabled(enabled);
    public LoadingWorkDiagnosticsSnapshot? Capture() => LoadingWorkRecorder.Snapshot;
    public void Scheduled(IDisposable handle, Delegate step, long frame, int pending) => LoadingWorkRecorder.Scheduled(handle, step, frame, pending);
    public void Disposed(IDisposable handle, long frame) => LoadingWorkRecorder.Disposed(handle, frame);
    public void Admission(IDisposable handle, string reason, long frame, int pending, double remaining) => LoadingWorkRecorder.Admission(handle, reason, frame, pending, remaining);
    public void FirstStep(IDisposable handle, long frame) => LoadingWorkRecorder.FirstStep(handle, frame);
    public void StepReturned(IDisposable handle, long frame, CooperativeWorkStatus status) => LoadingWorkRecorder.StepReturned(handle, frame, status);
    public void End(IDisposable handle, long frame, bool success) => LoadingWorkRecorder.End(handle, frame, success);
}
