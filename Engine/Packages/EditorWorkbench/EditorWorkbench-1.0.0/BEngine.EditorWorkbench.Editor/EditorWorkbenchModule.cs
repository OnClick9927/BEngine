using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Editor;

internal static class EditorWorkbenchModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeServiceContributions.Register(ConfigureServices);
        EditorCallbacks.LoopExecutionStopped += EditorLoopProtection.OnLoopStopped;
        EditorBuildEvents.buildStarted += OnBuildStarted;
        SettingsService.OpenRequested += OpenSettings;
        EditorPresentationRequests.PrefabsRefreshed += RefreshPrefabs;
        TypeCache.RegisterWarmup("ComponentContextMenus", ComponentContextMenuRegistry.Warmup);
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if(assembly != typeof(EditorWorkbenchModule).Assembly) return;
            EditorCallbacks.LoopExecutionStopped -= EditorLoopProtection.OnLoopStopped;
            EditorBuildEvents.buildStarted -= OnBuildStarted;
            SettingsService.OpenRequested -= OpenSettings;
            EditorPresentationRequests.PrefabsRefreshed -= RefreshPrefabs;
        });
    }
    private static void ConfigureServices(IServiceCollection services) =>
        services.TryAddSingleton<IBPackageArchiveService, BPackageArchiveService>();
    private static void OnBuildStarted(string _) => ConsoleLogController.ClearIfEnabled(ConsoleClearTrigger.Build);
    private static void OpenSettings(SettingsScope scope,string path)
    {
        if(scope==SettingsScope.User) PreferencesWindow.Open(path); else ProjectSettingsWindow.Open(path);
    }
    private static void RefreshPrefabs()
    {
        {
            var __typedSource1849 = EditorWindow.EnumerateOpenWindows();
            ArgumentNullException.ThrowIfNull(__typedSource1849, "source");
            foreach (var __typedItem1849 in __typedSource1849)
            {
                if (__typedItem1849 is not PrefabOverridesWindow window)
                    continue;
                window.controller.Refresh();
            }
        }
    }
}
