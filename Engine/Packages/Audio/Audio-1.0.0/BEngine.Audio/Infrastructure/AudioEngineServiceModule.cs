using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Audio;

public sealed class AudioEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context)
    {
        services.TryAddSingleton<IAudioOutputFactory>(AudioRuntimeServices.OutputFactory);
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISceneRuntimeSystem, AudioRuntimeSystem>());
    }
}
