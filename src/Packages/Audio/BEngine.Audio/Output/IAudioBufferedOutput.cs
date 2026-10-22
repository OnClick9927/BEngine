namespace BEngine.Audio;

/// <summary>A bounded output reports consumption and accepts a prefix instead of dropping old audio.</summary>
public interface IAudioBufferedOutput : IAudioOutput
{
    int capacityFrames { get; }
    int queuedFrames { get; }
    int availableFrames { get; }
    void SetExpectedSubmissionFrames(int frames);
    bool TrySubmit(ReadOnlySpan<float> samples, out int acceptedFrames);
}
