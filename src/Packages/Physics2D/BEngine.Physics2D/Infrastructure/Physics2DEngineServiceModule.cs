using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Physics2D;

public sealed class Physics2DEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddEnumerable(ServiceDescriptor.Transient<ISceneRuntimeSystem, PhysicsWorld2D>());
}
