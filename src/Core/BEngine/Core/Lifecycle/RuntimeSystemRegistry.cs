using Microsoft.Extensions.DependencyInjection;

namespace BEngine;

public static class RuntimeSystemRegistry
{
    private static readonly Dictionary<string, RuntimeSystemFactoryRegistration> Factories =
        new(StringComparer.Ordinal);

    public static void Register(string id, Func<ISceneRuntimeSystem> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        Register(id, _ => factory());
    }

    public static void Register(string id, Func<IServiceProvider, ISceneRuntimeSystem> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        Factories[id] = new RuntimeSystemFactoryRegistration(null, factory);
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
        Factories[id] = new RuntimeSystemFactoryRegistration(systemType, Create);
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Factories.Remove(id);
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var id in Factories.Where(pair => IsOwnedBy(pair.Value, assembly))
                     .Select(pair => pair.Key).ToArray())
            Factories.Remove(id);
    }

    private static bool IsOwnedBy(
        RuntimeSystemFactoryRegistration registration,
        System.Reflection.Assembly assembly) =>
        registration.SystemType?.Assembly == assembly ||
        registration.Factory.Method.DeclaringType?.Assembly == assembly ||
        registration.Factory.Method.ReturnType.Assembly == assembly;

    internal static ISceneRuntimeSystem[] CreateSystems(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var factories = Factories.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
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

    private static Type SystemTypeOf(ISceneRuntimeSystem system) =>
        system is ScopedSceneRuntimeSystem scoped ? scoped.SystemType : system.GetType();

}
