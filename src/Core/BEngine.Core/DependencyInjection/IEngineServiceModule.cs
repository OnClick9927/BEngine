using Microsoft.Extensions.DependencyInjection;

namespace BEngine.DependencyInjection;

public interface IEngineServiceModule
{
    void ConfigureServices(IServiceCollection services, EngineServiceContext context);
}
