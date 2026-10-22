namespace BEngine.Audio;

internal static class AudioRuntimeServices
{
    internal static IAudioOutputFactory OutputFactory =>
        RuntimeServiceAccess.GetRequiredService<IAudioOutputFactory>();
}
