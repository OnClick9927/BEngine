using BEngine.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Platform.DotNet;

public static class DotNetNetworkServices
{
    public static IServiceCollection AddBEngineDotNetNetworking(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IHttpTransport>(DotNetHttpTransport.Shared);
        services.TryAddSingleton<Func<IWebSocketTransport>>(static _ => static () => new DotNetWebSocketTransport());
        return services;
    }
}
