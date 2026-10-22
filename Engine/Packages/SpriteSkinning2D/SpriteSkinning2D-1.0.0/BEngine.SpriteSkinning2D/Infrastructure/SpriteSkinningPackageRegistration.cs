using System.Runtime.CompilerServices;
using System.Text;
using BEngine.Rendering;

namespace BEngine;

internal static class SpriteSkinningPackageRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        RuntimeAssetCodecRegistry.Register<SpriteSkinAsset>((bytes, _) =>
            SpriteSkinAsset.Deserialize(Encoding.UTF8.GetString(bytes.Span)));
        SceneRenderContributor2DRegistry.Register("com.bengine.sprite-skinning2d.2d",
            device => new SpriteSkinRenderContributor2D(device));
    }
}
