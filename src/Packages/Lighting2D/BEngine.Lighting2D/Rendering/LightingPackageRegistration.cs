using System.Runtime.CompilerServices;
using BEngine.Rendering;

namespace BEngine;

internal static class LightingPackageRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Registers the package rendering backend.")]
    internal static void Register()
    {
        SceneRenderContributor2DRegistry.Register("com.bengine.lighting2d", device => new LightingRenderContributor2D(device));
        SceneLightingShaderRegistry2D.Register("com.bengine.lighting2d", static device => new LightingShaderContext2D(device),
            static () => LightingShaderContext2D.ShaderFunctions);
    }
}
