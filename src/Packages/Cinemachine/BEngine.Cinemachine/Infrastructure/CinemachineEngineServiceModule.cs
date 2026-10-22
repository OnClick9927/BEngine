using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Cinemachine;

public sealed class CinemachineEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
    }
}
