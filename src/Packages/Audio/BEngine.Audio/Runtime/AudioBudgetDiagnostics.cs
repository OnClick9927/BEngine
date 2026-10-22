using System.Runtime.CompilerServices;
using BEngine.Diagnostics;

namespace BEngine.Audio;

internal static class AudioBudgetDiagnostics
{
    [ModuleInitializer]
    internal static void Register() => RuntimeDiagnosticRegistry.Register("budget.audio", "Audio budgets",
        new("reserved", "PCM loaded + in flight", RuntimeDiagnosticUnit.Bytes, () => AudioLoadingSettings.snapshot.ReservedPcmBytes),
        new("loaded", "Loaded PCM", RuntimeDiagnosticUnit.Bytes, () => AudioLoadingSettings.snapshot.LoadedPcmBytes),
        new("limit", "Effective PCM budget", RuntimeDiagnosticUnit.Bytes, () => AudioLoadingSettings.effectiveDecodedBudgetBytes),
        new("pending", "Pending loads", RuntimeDiagnosticUnit.Number, () => AudioLoadingSettings.snapshot.PendingRequests),
        new("decoders", "Active decoders", RuntimeDiagnosticUnit.Number, () => AudioLoadingSettings.snapshot.ActiveDecoders),
        new("real", "Real voices", RuntimeDiagnosticUnit.Number, () => AudioRuntimeSettings.voiceSnapshot.RealVoices),
        new("virtual", "Virtual voices", RuntimeDiagnosticUnit.Number, () => AudioRuntimeSettings.voiceSnapshot.VirtualVoices),
        new("voiceLimit", "Real voice budget", RuntimeDiagnosticUnit.Number, () => AudioRuntimeSettings.maximumRealVoices),
        new("queued", "Output queued frames", RuntimeDiagnosticUnit.Number, () => AudioOutput.initializedOutput is IAudioBufferedOutput output ? output.queuedFrames : 0),
        new("capacity", "Output capacity frames", RuntimeDiagnosticUnit.Number, () => AudioOutput.initializedOutput is IAudioBufferedOutput output ? output.capacityFrames : 0),
        new("backpressure", "Backpressured frames", RuntimeDiagnosticUnit.Number, () => AudioOutput.backpressuredFrames),
        new("dropped", "Output dropped frames", RuntimeDiagnosticUnit.Number, () => (AudioOutput.initializedOutput as IAudioOutputDiagnostics)?.droppedFrames ?? 0),
        new("underrunAvailable", "Backend observes underruns (0/1)", RuntimeDiagnosticUnit.Number, () => (AudioOutput.initializedOutput as IAudioOutputDiagnostics)?.underrunEvents is null ? 0 : 1),
        new("underruns", "Observed queue starvation events", RuntimeDiagnosticUnit.Number, () => (AudioOutput.initializedOutput as IAudioOutputDiagnostics)?.underrunEvents ?? 0));
}
