using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine;

public static class RuntimeSystemRegistry
{
    private static readonly Dictionary<string, List<RuntimeSystemFactoryRegistration>> Factories =
        new(StringComparer.Ordinal);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string id, Func<ISceneRuntimeSystem> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        Register(id, _ => factory(), factory.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string id, Func<IServiceProvider, ISceneRuntimeSystem> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        Register(id, factory, factory.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly());
    }

    public static void RegisterScoped(
        string id,
        IServiceScopeFactory scopeFactory,
        Type systemType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(systemType);
        if (!typeof(ISceneRuntimeSystem).IsAssignableFrom(systemType) || systemType.IsAbstract)
            throw new ArgumentException(
                $"{systemType.FullName} is not a concrete scene runtime system.", nameof(systemType));
        ISceneRuntimeSystem Create(IServiceProvider _)
        {
            var scope = scopeFactory.CreateScope();
            try
            {
                var system = (ISceneRuntimeSystem)scope.ServiceProvider.GetRequiredService(systemType);
                return new ScopedSceneRuntimeSystem(systemType, scope, system);
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }
        Register(id, Create, systemType.Assembly, systemType);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Factories.TryGetValue(id, out var registrations)) return false;
        var caller = Assembly.GetCallingAssembly();
        var index = registrations.FindLastIndex(registration => registration.Owner == caller);
        if (index < 0) index = registrations.Count - 1;
        registrations.RemoveAt(index);
        if (registrations.Count == 0) Factories.Remove(id);
        return true;
    }

    internal static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var (id, registrations) in Factories.ToArray())
        {
            registrations.RemoveAll(registration => registration.Owner == assembly);
            if (registrations.Count == 0) Factories.Remove(id);
        }
    }

    internal static bool HasRegistrationsFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Factories.Values.Any(registrations =>
            registrations.Any(registration => registration.Owner == assembly));
    }

    internal static ISceneRuntimeSystem[] CreateSystems(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var factories = Factories
            .Where(static pair => pair.Value.Count > 0)
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new KeyValuePair<string, RuntimeSystemFactoryRegistration>(
                pair.Key, pair.Value[^1]))
            .ToArray();
        var registered = new List<ISceneRuntimeSystem>();
        try
        {
            foreach (var factory in factories)
                registered.Add(factory.Value.Factory(services));
            if (services.GetService(typeof(IEnumerable<ISceneRuntimeSystem>)) is
                IEnumerable<ISceneRuntimeSystem> resolved)
                registered.AddRange(resolved);
            var registeredTypes = factories.Select(pair => pair.Value.SystemType)
                .Where(type => type is not null).Cast<Type>()
                .Concat(registered.Select(SystemTypeOf)).ToHashSet();
            foreach (var type in RuntimeTypeCache.GetRuntimeSystemTypes())
            {
                if (!registeredTypes.Add(type)) continue;
                registered.Add((ISceneRuntimeSystem)(services.GetService(type) ??
                    ActivatorUtilities.CreateInstance(services, type)));
            }

            var selected = registered.DistinctBy(SystemTypeOf)
                .Where(system => IsActiveType(SystemTypeOf(system)))
                .Where(system => RuntimePackageState.IsEnabled(system.packageId))
                .OrderBy(system => system.order)
                .ThenBy(system => system.GetType().FullName, StringComparer.Ordinal)
                .ToArray();
            foreach (var scoped in registered.OfType<ScopedSceneRuntimeSystem>()
                         .Where(scoped => !selected.Contains(scoped)))
                scoped.DisposeScope();
            return selected;
        }
        catch
        {
            foreach (var scoped in registered.OfType<ScopedSceneRuntimeSystem>())
            {
                try { scoped.DisposeScope(); }
                catch { }
            }
            throw;
        }
    }

    internal static bool TryCreateSystem(
        string id,
        IServiceProvider services,
        out ISceneRuntimeSystem system)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(services);
        if (!Factories.TryGetValue(id, out var registrations) || registrations.Count == 0)
        {
            system = null!;
            return false;
        }
        system = registrations[^1].Factory(services);
        return true;
    }

    private static Type SystemTypeOf(ISceneRuntimeSystem system) =>
        system is ScopedSceneRuntimeSystem scoped ? scoped.SystemType : system.GetType();

    private static bool IsActiveType(Type type)
    {
        // Host DI may retain an AOT package instance after a release replaces
        // that package. Only the active domain's same-name system may run;
        // otherwise it queries components from a different load context.
        var active = type.FullName is { } name ? RuntimeTypeCache.FindType(name) : null;
        return active is null || ReferenceEquals(active, type);
    }

    private static void Register(
        string id,
        Func<IServiceProvider, ISceneRuntimeSystem> factory,
        Assembly owner,
        Type? systemType = null)
    {
        if (!Factories.TryGetValue(id, out var registrations))
            Factories.Add(id, registrations = []);
        registrations.RemoveAll(registration => registration.Owner == owner);
        registrations.Add(new RuntimeSystemFactoryRegistration(systemType, factory, owner));
    }

}
