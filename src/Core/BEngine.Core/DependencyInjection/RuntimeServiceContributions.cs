using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
namespace BEngine.DependencyInjection;
public static class RuntimeServiceContributions
{
    private static readonly List<Action<IServiceCollection, IServiceProvider>> PlatformState = [];
    public static void RegisterPlatformState(Action<IServiceCollection, IServiceProvider> share)
    {
        ArgumentNullException.ThrowIfNull(share);
        if (!PlatformState.Contains(share)) PlatformState.Add(share);
    }
    public static void SharePlatformState(IServiceCollection services, IServiceProvider host)
    {
        foreach (var share in PlatformState) share(services, host);
    }
    public static int Version { get; private set; }
    private static readonly List<Action<IServiceCollection>> Registrations = [];
    private static readonly Dictionary<Type, List<DefaultRegistration>> Defaults = [];
    [ThreadStatic] private static HashSet<IServiceCollection>? _configuring;
    public static void Register(Action<IServiceCollection> configure) { ArgumentNullException.ThrowIfNull(configure); Registrations.RemoveAll(r => r.Method == configure.Method); Registrations.Add(configure); Version++; }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RegisterDefault<T>(T service) where T : class
    {
        ArgumentNullException.ThrowIfNull(service);
        var owner = Assembly.GetCallingAssembly();
        if (!Defaults.TryGetValue(typeof(T), out var registrations)) Defaults[typeof(T)] = registrations = [];
        registrations.RemoveAll(registration => registration.Owner == owner);
        registrations.Add(new DefaultRegistration(service, owner));
        Version++;
    }
    public static T? GetDefault<T>() where T : class =>
        Defaults.TryGetValue(typeof(T), out var registrations) && registrations.Count > 0 ? registrations[^1].Service as T : null;
    public static void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var configuring = _configuring ??= new(ReferenceEqualityComparer.Instance);
        // A contribution may register prerequisites through another AddBEngine* method.
        // The outer pass owns registration for this collection, including modules loaded
        // lazily while a contribution's method is being compiled or invoked.
        if (!configuring.Add(services)) return;
        try
        {
            var snapshot = Registrations.ToArray();
            var version = Version;
            foreach (var configure in snapshot) configure(services);
            if (Version == version) return;

            // Keep the stable path at one snapshot allocation. Only dynamic registration
            // needs a visited set; each method runs once, even if it registers itself again.
            var invoked = new HashSet<MethodInfo>();
            foreach (var configure in snapshot) invoked.Add(configure.Method);
            while (true)
            {
                var progressed = false;
                foreach (var configure in Registrations.ToArray())
                {
                    if (!invoked.Add(configure.Method)) continue;
                    configure(services);
                    progressed = true;
                }
                if (!progressed) break;
            }
        }
        finally { configuring.Remove(services); }
    }
    public static void UnregisterAssembly(Assembly assembly)
    {
        PlatformState.RemoveAll(share => share.Method.DeclaringType?.Assembly == assembly);
        Registrations.RemoveAll(r => r.Method.DeclaringType?.Assembly == assembly);
        Version++;
        foreach (var pair in Defaults.ToArray())
        {
            pair.Value.RemoveAll(registration => pair.Key.Assembly == assembly || registration.Owner == assembly ||
                registration.Service.GetType().Assembly == assembly);
            if (pair.Value.Count == 0) Defaults.Remove(pair.Key);
        }
    }
    private sealed record DefaultRegistration(object Service, Assembly Owner);
}
