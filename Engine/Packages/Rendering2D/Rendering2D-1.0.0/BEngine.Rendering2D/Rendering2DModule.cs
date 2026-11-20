using System.Runtime.CompilerServices;
using BEngine.DependencyInjection;
using BEngine.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace BEngine;

public sealed class Rendering2DModule : IEngineServiceModule
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        RuntimeServiceContributions.Register(static services => services.TryAddSingleton<ISceneRendererFactory, PortableSceneRendererFactory>());
        RuntimeObjectCodecs.Register(new SpriteReferenceCodec());
        RuntimeResourceCodecs.Register(new SpriteResourceCodec());
        RuntimeAssemblyCleanup.Register(static assembly =>
        {
            SpriteSourceRegistry.UnregisterAssembly(assembly);
            ScenePostProcessor2DRegistry.UnregisterAssembly(assembly);
            SceneLightingShaderRegistry2D.UnregisterAssembly(assembly);
            SpriteReferenceCodec.Clear();
        });
    }
    public void ConfigureServices(IServiceCollection services, EngineServiceContext context) =>
        services.TryAddSingleton<ISceneRendererFactory, PortableSceneRendererFactory>();
}
