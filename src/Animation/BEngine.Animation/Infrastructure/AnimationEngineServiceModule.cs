using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Animation;

public sealed class AnimationEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<ISceneRuntimeSystem, AnimationRuntimeSystem>());
}
