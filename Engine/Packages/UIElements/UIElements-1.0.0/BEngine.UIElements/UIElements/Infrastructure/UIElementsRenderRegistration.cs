using System.Runtime.CompilerServices;
using BEngine.Rendering;
using BEngine.Rendering.Rhi;

namespace BEngine.UIElements;

internal static class UIElementsRenderRegistration
{
    // A native AOT document can remain alive while a hot-loaded copy of this
    // package owns the incoming scene. Each copy can only query its own
    // UIDocument type. Replacing the native contributor would hide that UI
    // before the scene transition has completed. Assembly ownership still
    // lets the registry release the matching contributor on domain unload.
    private static readonly string RegistrationId = "com.bengine.ui-elements.2d/" +
        RuntimeHelpers.GetHashCode(typeof(UIDocument).Assembly).ToString("X");

    [ModuleInitializer]
    internal static void Register() => SceneRenderContributor2DRegistry.Register(
        RegistrationId, device => new UIElementsRenderContributor2D(device));
}
