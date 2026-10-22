namespace BEngine.Audio;

public interface IAudioOutputFactory
{
    IAudioOutput CreateDefault();
    IAudioOutput SilentOutput { get; }
    bool IsSystemOutput(IAudioOutput output);
}
