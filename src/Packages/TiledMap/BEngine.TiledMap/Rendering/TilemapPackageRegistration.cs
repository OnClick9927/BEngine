using System.Runtime.CompilerServices;
using BEngine.Documents;
using BEngine.Rendering;

namespace BEngine.TiledMap;

internal static class TilemapPackageRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        ComponentTypeMigrationRegistry.Register("BEngine.Tilemap", typeof(Tilemap).FullName!);
        ComponentTypeMigrationRegistry.Register("BEngine.TilemapRenderer", typeof(TilemapRenderer).FullName!);
        SceneRenderContributor2DRegistry.Register(
            "com.bengine.tiledmap.2d", device => new TilemapRenderContributor2D(device));
    }
}
