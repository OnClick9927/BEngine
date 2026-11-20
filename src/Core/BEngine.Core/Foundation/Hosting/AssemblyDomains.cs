using BEngine.DependencyInjection;

namespace BEngine.Hosting;

public static class AssemblyDomains
{
    public static IAssemblyDomainService? Available => RuntimeServices.GetService<IAssemblyDomainService>() ??
        RuntimeServiceContributions.GetDefault<IAssemblyDomainService>();

    public static IAssemblyDomainService Current => Available ?? throw new PlatformNotSupportedException(
        "The platform host has not registered an assembly domain service.");
}
