using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.AssetBundles;
public static class AssetBundleServices
{
    [ModuleInitializer] internal static void Initialize()
    {
        RuntimeServiceContributions.Register(Configure);
    }
    private static void Configure(IServiceCollection services)
    {
        // The factory requires networking even before content initialization. Do not
        // depend on lazy module initialization or a later platform-registration pass.
        BEngine.Networking.NetworkingServices.Configure(services);
        services.TryAddSingleton<IAssetBundleManagerFactory, AssetBundleManagerFactory>();
    }
}
