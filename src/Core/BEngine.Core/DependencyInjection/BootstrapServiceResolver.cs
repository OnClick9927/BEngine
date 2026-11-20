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
        var registration = Scan659(services);
        
        global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor? Scan659(global::System.Collections.Generic.IEnumerable<global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor> scanSource659)
        {
            ArgumentNullException.ThrowIfNull(scanSource659, "source");
            if (scanSource659 is global::System.Collections.Generic.IList<global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor> scanList659)
            {
                for (var scanIndex659 = scanList659.Count - 1; scanIndex659 >= 0; scanIndex659--)
                {
                    var scanItem659 = scanList659[scanIndex659];
                    if (Match659(scanItem659))
                        return scanItem659;
                }
        
                return default!;
            }
        
            global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor? result = default!;
            foreach (var scanItem659 in scanSource659)
                if (Match659(scanItem659))
                    result = scanItem659;
            return result;
            bool Match659(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor item) => item.ServiceType == serviceType && !item.IsKeyedService;
        }
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
