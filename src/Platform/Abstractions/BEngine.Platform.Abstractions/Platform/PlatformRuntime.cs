using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Platform;

/// <summary>Installed once by the platform host before any engine API is used.</summary>
public static class PlatformRuntime
{
    private static IServiceProvider? _services;

    public static void Initialize(IServiceProvider services) =>
        _services = services ?? throw new ArgumentNullException(nameof(services));

    public static T GetRequiredService<T>() where T : class =>
        GetService<T>() ?? throw new PlatformNotSupportedException(
            $"The platform host has not registered {typeof(T).Name}. Initialize a BEngine platform adapter first.");

    public static T? GetService<T>() where T : class => _services?.GetService<T>() ?? BEngine.DependencyInjection.RuntimeServiceContributions.GetDefault<T>();
}
