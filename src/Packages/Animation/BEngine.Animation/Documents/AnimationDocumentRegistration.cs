using System.Runtime.CompilerServices;
using BEngine.Documents;

namespace BEngine.Animation;

internal static class AnimationDocumentRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        DocumentConversionRegistry.Register(new AnimationClipDocumentConverter());
        DocumentConversionRegistry.Register(new AnimatorControllerDocumentConverter());
        DocumentValidationRegistry.Register<AnimationClipDocument>(AnimationClipDocumentConverter.Validate);
        DocumentValidationRegistry.Register<AnimatorControllerDocument>(AnimatorControllerDocumentConverter.Validate);
    }
}
