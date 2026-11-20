namespace BEngine.Audio;

internal static class AudioRuntimeServices
{
    internal static IAudioOutputFactory OutputFactory =>
        RuntimeServices.GetRequiredService<IAudioOutputFactory>();
}
