using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor.Build;

internal static class PlayerBuildServices
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register engine build capabilities.")]
    internal static void Initialize() => RuntimeServiceContributions.Register(services =>
        services.TryAddSingleton<PlatformPlayerBuildCapabilities>());
}
