using BEngine.Instrumentation;
using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform;

internal static class ManagedHeapPlatformRegistration
{
    private static readonly IManagedHeapReader Reader = new ClrManagedHeapReader();
    [ModuleInitializer]
    internal static void Initialize()
    {
        RuntimeServiceContributions.RegisterDefault(Reader);
        RuntimeServiceContributions.Register(Configure);
    }
    private static void Configure(IServiceCollection services) => services.TryAddSingleton(Reader);
}
