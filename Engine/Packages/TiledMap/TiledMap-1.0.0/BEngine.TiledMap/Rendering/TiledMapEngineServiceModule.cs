using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.TiledMap;

public sealed class TiledMapEngineServiceModule : IEngineServiceModule
{
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddSingleton<ITilemapTextureSource, FileTilemapTextureSource>();
}
