using System.Runtime.CompilerServices;
using BEngine.Rendering;

namespace BEngine.TiledMap;

internal static class TilemapPackageRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        SceneRenderContributor2DRegistry.Register(
            "com.bengine.tiledmap.2d", device => new TilemapRenderContributor2D(device));
    }
}
