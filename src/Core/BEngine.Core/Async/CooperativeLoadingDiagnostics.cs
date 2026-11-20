namespace BEngine;
internal static class CooperativeLoadingDiagnostics
{
    private static ILoadingWorkDiagnostics? Sink => DependencyInjection.RuntimeServiceContributions.GetDefault<ILoadingWorkDiagnostics>();
    internal static bool Enabled => Sink?.Enabled == true;
    internal static void SetEnabled(bool value) => Sink?.SetEnabled(value);
    internal static LoadingWorkDiagnosticsSnapshot? Snapshot => Sink?.Capture();
    internal static void Scheduled(IDisposable handle, Delegate step, long frame, int pending) => Sink?.Scheduled(handle, step, frame, pending);
    internal static void Disposed(IDisposable handle, long frame) => Sink?.Disposed(handle, frame);
    internal static void Admission(IDisposable handle, string reason, long frame, int pending, double remaining) => Sink?.Admission(handle, reason, frame, pending, remaining);
    internal static void FirstStep(IDisposable handle, long frame) => Sink?.FirstStep(handle, frame);
    internal static void StepReturned(IDisposable handle, long frame, CooperativeWorkStatus status) => Sink?.StepReturned(handle, frame, status);
    internal static void End(IDisposable handle, long frame, bool success) => Sink?.End(handle, frame, success);
}
