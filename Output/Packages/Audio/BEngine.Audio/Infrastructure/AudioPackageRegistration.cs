using System.Runtime.CompilerServices;

namespace BEngine.Audio;

internal static class AudioPackageRegistration
{
    [ModuleInitializer]
    internal static void RegisterCodec()
    {
        RuntimeAssetCodecRegistry.Register<AudioClip>(AudioClip.FromWav);
        Application.quitting += AudioOutput.Shutdown;
    }
}
