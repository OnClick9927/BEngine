using System.Net;
using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Instrumentation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform.DotNet.Diagnostics;

internal static class DiagnosticsPlatformRegistration
{
    private static readonly IRuntimeDiagnosticsPlatform Runtime = new SystemDiagnosticsPlatform();

    [ModuleInitializer]
    internal static void Initialize()
    {
        RuntimeServiceContributions.RegisterDefault(Runtime);
        RuntimeServiceContributions.Register(Configure);
    }

    private static void Configure(IServiceCollection services)
    {
        services.TryAddSingleton(Runtime);
    }
}

