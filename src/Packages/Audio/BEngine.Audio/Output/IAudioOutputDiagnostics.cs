namespace BEngine.Audio;

/// <summary>Device queue counters. Completion confirms the driver consumed a buffer, not acoustic loopback.</summary>
public interface IAudioOutputDiagnostics
{
    string backend { get; }
    long submittedFrames { get; }
    long completedFrames { get; }
    long droppedFrames { get; }
    int pendingBuffers { get; }
    /// <summary>Null when the backend cannot observe queue starvation. This is not an acoustic measurement.</summary>
    long? underrunEvents => null;
}
