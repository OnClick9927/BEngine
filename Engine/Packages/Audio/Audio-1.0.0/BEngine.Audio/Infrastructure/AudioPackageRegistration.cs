using System.Runtime.CompilerServices;

namespace BEngine.Audio;

internal static class AudioPackageRegistration
{
    [ModuleInitializer]
    internal static void RegisterCodec()
    {
        RuntimeAssetCodecRegistry.RegisterPrepared<AudioClip, AudioClip.PreparedAudio>(
            AudioClip.FromAudio, AudioClip.PrepareAudio, AudioClip.CommitAudio);
        Application.quitting += AudioOutput.Shutdown;
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if (assembly == typeof(AudioOutput).Assembly) AudioOutput.Shutdown();
        });
    }
}
