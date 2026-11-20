using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Networking;
public static class NetworkingServices
{
    [ModuleInitializer] internal static void Initialize()
    {
        RuntimeServiceContributions.RegisterDefault<ISocketTransportFactory>(SocketTransportFactory.Shared);
        RuntimeServiceContributions.RegisterDefault<IHttpTransport>(PlatformHttpTransport.Shared);
        RuntimeServiceContributions.RegisterDefault<Func<IWebSocketTransport>>(static () => new PlatformWebSocketTransport());
        RuntimeServiceContributions.RegisterDefault<IHttpNetworkRequestFactory>(HttpNetworkRequestFactory.Shared);
        RuntimeServiceContributions.Register(Configure);
    }
    /// <summary>Configures networking prerequisites explicitly without replacing host overrides.</summary>
    public static void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISocketTransportFactory>(SocketTransportFactory.Shared);
        services.TryAddSingleton<IHttpNetworkRequestFactory>(HttpNetworkRequestFactory.Shared);
        services.TryAddSingleton<IHttpTransport>(PlatformHttpTransport.Shared);
        services.TryAddSingleton<Func<IWebSocketTransport>>(static _ => static () => new PlatformWebSocketTransport());
    }
}
