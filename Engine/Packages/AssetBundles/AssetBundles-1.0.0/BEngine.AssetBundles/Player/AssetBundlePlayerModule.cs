using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using BEngine.Player;
namespace BEngine.AssetBundles;
internal static class AssetBundlePlayerModule
{
    [ModuleInitializer]
    internal static void Register()
    {
        if (typeof(AssetBundlePlayerModule).Assembly.IsCollectible) return;
        PlayerHostRegistry.RegisterContentWorkflow(AssetBundlePlayerWorkflow.Shared);
        PlayerContentDiagnostics.Capture = static () =>
        {
            var snapshot = AssetBundlePerformanceDiagnostics.Snapshot;
            return new(snapshot.Enabled, snapshot.Counters.Select(static c => new PlayerContentPerformanceCounter(
                c.Name, c.Calls, c.Bytes, c.TotalMilliseconds, c.MaximumMilliseconds)).ToArray());
        };
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            if (assembly != typeof(AssetBundlePlayerModule).Assembly) return;
            PlayerHostRegistry.Unregister(assembly);
            PlayerContentDiagnostics.Capture = null;
        });
    }
}
