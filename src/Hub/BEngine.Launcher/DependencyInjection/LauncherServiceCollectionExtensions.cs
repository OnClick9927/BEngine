using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Launcher;

public static class LauncherServiceCollectionExtensions
{
    public static IServiceCollection AddBEngineLauncher(
        this IServiceCollection services,
        string editorPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new LauncherOptions(Path.GetFullPath(editorPath));
        services.AddBEngine(new EngineServiceContext(EngineHostKind.Launcher, InstanceName: "BEngine Hub"),
            [typeof(BObject).Assembly, typeof(LauncherServiceCollectionExtensions).Assembly]);
        services.TryAddSingleton(options);
        services.TryAddSingleton(_ => new LauncherHistoryStore(
            BEngine.Editor.EditorDataPaths.launcherSettingsPath));
        services.TryAddSingleton(_ => new LauncherPackageCatalog());
        services.TryAddSingleton(_ => new LauncherProjectService());
        services.TryAddTransient(serviceProvider => new ProjectLauncherForm(
            serviceProvider.GetRequiredService<LauncherOptions>(),
            serviceProvider.GetRequiredService<LauncherHistoryStore>(),
            serviceProvider.GetRequiredService<LauncherPackageCatalog>(),
            serviceProvider.GetRequiredService<LauncherProjectService>()));
        return services;
    }
}
