using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Navigation2D;

public sealed class NavigationEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<ISceneRuntimeSystem, NavigationRuntimeSystem>());
}
