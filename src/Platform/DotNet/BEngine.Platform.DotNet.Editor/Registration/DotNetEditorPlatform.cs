using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Editor;
using BEngine.ProjectSystem.Editor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Platform;
public static class DotNetEditorPlatform
{
    [ModuleInitializer]
    public static void Initialize()
    {
        EditorPresentationServices.Initialize();
        if (OperatingSystem.IsWindows()) RuntimeServiceContributions.RegisterDefault<IEditorColorManagementHost>(WindowsColorManagementHost.Shared);
        RuntimeServiceContributions.Register(Configure);
    }
    public static void Configure(IServiceCollection services)
    {
        if (OperatingSystem.IsWindows()) services.TryAddSingleton<IEditorColorManagementHost>(WindowsColorManagementHost.Shared);
        services.Replace(ServiceDescriptor.Singleton<BEngine.Platform.IApplicationPaths, EditorApplicationPaths>());
        services.TryAddSingleton<IAssemblyLoadingService, SystemAssemblyLoadingService>();
        services.TryAddSingleton<IProcessRunner>(SystemProcessRunner.Shared);
        services.TryAddSingleton<IProcessHost>(SystemProcessHost.Shared);
        services.TryAddSingleton<IEditorHttpListenerFactory>(SystemEditorHttpListenerFactory.Shared);
        services.TryAddSingleton<IEditorHostTools>(SystemEditorHostTools.Shared);
        services.TryAddSingleton<IEditorWaitService, SystemEditorWaitService>();
        services.TryAddSingleton<IEditorExecutionEnvironment, SystemEditorExecutionEnvironment>();
        services.TryAddSingleton<IEditorLogTimestampParser, SystemEditorLogTimestampParser>();
        services.TryAddSingleton<IEditorHostOutput, SystemEditorHostOutput>();
        services.TryAddSingleton<IProjectSourceChangeMonitorFactory, SystemProjectSourceChangeMonitorFactory>();
        services.TryAddSingleton<BEngine.ProjectSystem.IPackageCatalogSource, HubPackageCatalogSource>();
        services.TryAddSingleton<ProcessUtility>();
        services.TryAddSingleton<ProcessHostUtility>();
        services.TryAddSingleton<WaitUtility>();
        services.TryAddSingleton<ExecutionUtility>();
        services.TryAddSingleton<LogTimestampUtility>();
        services.TryAddSingleton<HostOutputUtility>();
        services.TryAddSingleton<AssemblyLoadingUtility>();
        services.TryAddSingleton<SourceMonitorUtility>();
    }
}
