using System.Runtime.CompilerServices;
using System.Text;
using BEngine.Rendering;

namespace BEngine;

internal static class SpriteShapePackageRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeAssetCodecRegistry.Register<SpriteShapeAsset>((bytes, _) =>
            SpriteShapeAsset.Deserialize(Encoding.UTF8.GetString(bytes.Span)));
        SceneRenderContributor2DRegistry.Register("com.bengine.sprite-shape2d.2d",
            device => new SpriteShapeRenderContributor2D(device));
    }
}
