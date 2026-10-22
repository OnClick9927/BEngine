using Microsoft.Extensions.DependencyInjection;

namespace BEngine.DependencyInjection;

/// <summary>Materializes singleton services needed before the host container is built.</summary>
public sealed class BootstrapServiceResolver(IServiceCollection services) : IServiceProvider
{
    private readonly HashSet<Type> _resolving = [];

    public T GetRequiredService<T>() where T : class =>
        (T)(GetService(typeof(T)) ?? throw new InvalidOperationException($"Missing bootstrap service '{typeof(T)}'."));

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider)) return this;
        var registration = services.LastOrDefault(item => item.ServiceType == serviceType && !item.IsKeyedService);
        if (registration is null) return null;
        if (registration.ImplementationInstance is { } instance) return instance;
        if (registration.Lifetime != ServiceLifetime.Singleton)
            throw new InvalidOperationException($"Bootstrap service '{serviceType}' must be a singleton.");
        if (!_resolving.Add(serviceType))
            throw new InvalidOperationException($"Circular bootstrap service dependency: '{serviceType}'.");
        try
        {
            var result = registration.ImplementationFactory is { } factory
                ? factory(this)
                : ActivatorUtilities.CreateInstance(this, registration.ImplementationType!);
            // Bootstrap services are borrowed configuration services, like instance registrations.
            // Do not capture disposable services outside the container that owns their lifetime.
            if (result is IDisposable or IAsyncDisposable)
            {
                if (result is IDisposable disposable) disposable.Dispose();
                else ((IAsyncDisposable)result).DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw new InvalidOperationException($"Disposable bootstrap service '{serviceType}' must be supplied as a host-owned instance.");
            }
            services[services.IndexOf(registration)] = ServiceDescriptor.Singleton(serviceType, result);
            return result;
        }
        finally { _resolving.Remove(serviceType); }
    }
}
