using System.Reflection;
using BEngine.Entities;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.DependencyInjection;

public static class EngineServiceCollectionExtensions
{
    public static IServiceCollection AddBEngine(
        this IServiceCollection services,
        EngineServiceContext context,
        IEnumerable<Assembly>? assemblies = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);

        services.TryAddSingleton(context);
        services.TryAddSingleton<ISceneRuntimeFactory, SceneRuntimeFactory>();
        services.TryAddSingleton<RuntimeSceneManager>();
        services.TryAddSingleton<IRuntimeSceneManager>(static provider =>
            provider.GetRequiredService<RuntimeSceneManager>());
        var types = DiscoverTypes(assemblies);
        ConfigureModules(services, context, types);
        RegisterRuntimeSystems(services, types);
        return services;
    }

    private static Type[] DiscoverTypes(IEnumerable<Assembly>? assemblies)
    {
        if (assemblies is null)
        {
            RuntimeTypeCache.Warmup();
            return RuntimeTypeCache.GetAllTypes();
        }

        return assemblies.Distinct().SelectMany(GetLoadableTypes).Distinct().ToArray();
    }

    private static void ConfigureModules(
        IServiceCollection services,
        EngineServiceContext context,
        IEnumerable<Type> types)
    {
        foreach (var type in types.Where(IsConcrete<IEngineServiceModule>)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (Activator.CreateInstance(type) is not IEngineServiceModule module)
                throw new InvalidOperationException(
                    $"Engine service module '{type.FullName}' must have a public parameterless constructor.");
            module.ConfigureServices(services, context);
        }
    }

    private static void RegisterRuntimeSystems(IServiceCollection services, IEnumerable<Type> types)
    {
        foreach (var type in types.Where(IsConcrete<ISystem>)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
            services.TryAdd(ServiceDescriptor.Transient(type, type));

        foreach (var type in types.Where(IsConcrete<ISceneRuntimeSystem>)
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            services.TryAdd(ServiceDescriptor.Transient(type, type));
            services.TryAddEnumerable(ServiceDescriptor.Transient(typeof(ISceneRuntimeSystem), type));
        }
    }

    private static bool IsConcrete<T>(Type type) =>
        type is { IsClass: true, IsAbstract: false } && (type.IsPublic || type.IsNestedPublic) &&
        typeof(T).IsAssignableFrom(type);

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }
}
