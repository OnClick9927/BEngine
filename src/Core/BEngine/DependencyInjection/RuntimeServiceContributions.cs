using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
namespace BEngine.DependencyInjection;
public static class RuntimeServiceContributions
{
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
        // A contribution may register its prerequisites through another AddBEngine* method.
        // The outer pass already visits every contribution for this collection.
        if (!configuring.Add(services)) return;
        try { foreach (var configure in Registrations.ToArray()) configure(services); }
        finally { configuring.Remove(services); }
    }
    public static void UnregisterAssembly(Assembly assembly)
    {
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
