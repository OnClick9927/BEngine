using System.Runtime.CompilerServices;
using BEngine.Rendering;

namespace BEngine.PostProcessing;

internal static class PostProcessingRegistration
{
    [ModuleInitializer]
    internal static void Register() => ScenePostProcessor2DRegistry.Register(
        "bengine.postprocessing.2d", device => new PostProcessRenderer2D(device));
}
