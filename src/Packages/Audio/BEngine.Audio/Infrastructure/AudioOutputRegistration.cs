using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Audio;
internal static class AudioOutputRegistration
{
    [ModuleInitializer] internal static void Register()
    {
        IAudioOutputFactory factory = PlatformAudioOutputFactory.Shared;
        RuntimeServiceContributions.RegisterDefault(factory);
        RuntimeServiceContributions.Register(services => services.TryAddSingleton(factory));
    }
}
