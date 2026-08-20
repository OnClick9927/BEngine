using System.Runtime.CompilerServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.UIElements;

internal static class UIElementsPackageRegistration
{
    [ModuleInitializer]
    internal static void Register() => SceneRenderContributor2DRegistry.Register(
        "com.bengine.ui-elements.2d", device => new UIElementsRenderContributor2D(device));
}
