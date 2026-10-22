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
        services.TryAddSingleton<IAssetBundleManagerFactory, AssetBundleManagerFactory>();
    }
}
