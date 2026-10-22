using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Platform;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Audio;
internal static class AudioOutputRegistration
{
    [ModuleInitializer] internal static void Register()
    {
        IAudioOutputFactory factory = OperatingSystem.IsBrowser()
            ? WebAudioOutputFactory.Shared
            : OperatingSystem.IsAndroid() ? AndroidAudioOutputFactory.Shared
            : OperatingSystem.IsIOS() ? IosAudioOutputFactory.Shared
            : OperatingSystem.IsWindows() ? new WindowsAudioOutputFactory()
            : OpenAlAudioOutputFactory.Shared;
        RuntimeServiceContributions.RegisterDefault(factory);
        RuntimeServiceContributions.Register(services => services.TryAddSingleton(factory));
    }
}
