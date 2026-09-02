using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Audio;

public sealed class AudioEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISceneRuntimeSystem, AudioRuntimeSystem>());
}

internal static class AudioPackageRegistration
{
    [ModuleInitializer]
    internal static void RegisterCodec()
    {
        RuntimeAssetCodecRegistry.Register<AudioClip>(AudioClip.FromWav);
        Application.quitting += AudioOutput.Shutdown;
    }
}
