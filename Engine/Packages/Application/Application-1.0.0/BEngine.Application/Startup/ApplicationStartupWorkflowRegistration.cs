using System.Runtime.CompilerServices;
using BEngine.AssetBundles;
namespace BEngine.App.Startup;

internal static class ApplicationStartupWorkflowRegistration
{
    // A developer registration at the normal priority overrides the default framework workflow.
    private static IDisposable? _registration;
    [ModuleInitializer]
    internal static void Register()
    {
        if (typeof(ApplicationStartupWorkflowRegistration).Assembly.IsCollectible) return;
        _registration = AssetBundleStartupWorkflows.Register(static context => new AotStartupFlow(context), priority: -100);
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if (assembly != typeof(ApplicationStartupWorkflowRegistration).Assembly) return;
            _registration?.Dispose();
            _registration = null;
        });
    }
}
