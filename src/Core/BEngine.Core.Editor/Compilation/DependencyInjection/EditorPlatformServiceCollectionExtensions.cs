using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BEngine.DependencyInjection;
using BEngine.Platform;
using BEngine.ProjectSystem.Editor;

namespace BEngine.Editor;

public static class EditorPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineEditorPlatformServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EditorServiceRegistrationCatalog.Register(services);
        services.AddBEnginePlatformServices();
        services.TryAddSingleton<ProcessUtility>();
        services.TryAddSingleton<ProcessHostUtility>();
        services.TryAddSingleton<WaitUtility>();
        services.TryAddSingleton<ExecutionUtility>();
        services.TryAddSingleton<LogTimestampUtility>();
        services.TryAddSingleton<HostOutputUtility>();
        services.TryAddSingleton(provider => new ShaderCompilerUtility(provider.GetRequiredService<IShaderCompiler>()));
        services.TryAddSingleton<SourceMonitorUtility>();
        services.TryAddSingleton<AssemblyLoadingUtility>();
        return services;
    }
}
