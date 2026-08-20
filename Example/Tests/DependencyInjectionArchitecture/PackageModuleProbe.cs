using BEngine.DependencyInjection;

namespace BEngine.ExampleTests.DependencyInjectionArchitecture;

public sealed class PackageModuleProbe(EngineServiceContext context)
{
    public EngineServiceContext Context { get; } = context;
}
