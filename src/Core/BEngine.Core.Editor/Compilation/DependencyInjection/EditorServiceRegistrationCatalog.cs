using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

// Captured when the host first attaches, after its collection has been configured.
// Descriptors let late contributions distinguish inherited registrations without
// constructing every host service or retaining the mutable collection here.
internal sealed class EditorServiceRegistrationCatalog(ServiceDescriptor[] registrations)
{
    internal IReadOnlyList<ServiceDescriptor> Registrations => registrations;

    internal bool Contains(ServiceDescriptor candidate)
    {
        foreach (var registration in registrations)
        {
            if (registration.ServiceType != candidate.ServiceType || registration.Lifetime != candidate.Lifetime ||
                registration.IsKeyedService != candidate.IsKeyedService ||
                !Equals(registration.ServiceKey, candidate.ServiceKey)) continue;
            if (registration.IsKeyedService)
            {
                if (registration.KeyedImplementationType == candidate.KeyedImplementationType &&
                    Equals(registration.KeyedImplementationFactory, candidate.KeyedImplementationFactory) &&
                    ReferenceEquals(registration.KeyedImplementationInstance, candidate.KeyedImplementationInstance)) return true;
            }
            else if (registration.ImplementationType == candidate.ImplementationType &&
                     Equals(registration.ImplementationFactory, candidate.ImplementationFactory) &&
                     ReferenceEquals(registration.ImplementationInstance, candidate.ImplementationInstance)) return true;
        }
        return false;
    }

    internal static void Register(IServiceCollection services) => services.TryAddSingleton(
        _ => new EditorServiceRegistrationCatalog(services.ToArray()));
}
