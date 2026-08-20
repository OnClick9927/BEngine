using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class ProbeEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context)
    {
        services.AddScoped<ScopedProbe>();
        services.AddSingleton<LegacyRuntimeObservation>();
        services.AddSingleton(new PackageModuleProbe(context));
    }
}
