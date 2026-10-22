using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Platform.DotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine.Networking;
public static class NetworkingServices
{
    [ModuleInitializer] internal static void Initialize()
    {
        RuntimeServiceContributions.RegisterDefault<ISocketTransportFactory>(SocketTransportFactory.Shared);
        RuntimeServiceContributions.RegisterDefault<IHttpTransport>(DotNetHttpTransport.Shared);
        RuntimeServiceContributions.RegisterDefault<Func<IWebSocketTransport>>(static () => new DotNetWebSocketTransport());
        RuntimeServiceContributions.RegisterDefault<IHttpNetworkRequestFactory>(HttpNetworkRequestFactory.Shared);
        RuntimeServiceContributions.Register(Configure);
    }
    private static void Configure(IServiceCollection services)
    {
        services.TryAddSingleton<ISocketTransportFactory>(SocketTransportFactory.Shared);
        services.TryAddSingleton<IHttpNetworkRequestFactory>(HttpNetworkRequestFactory.Shared);
        services.AddBEngineDotNetNetworking();
    }
}
