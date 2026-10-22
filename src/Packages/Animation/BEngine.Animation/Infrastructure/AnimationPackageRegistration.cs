using System.Runtime.CompilerServices;
namespace BEngine;

internal static class AnimationPackageRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package codecs when its assembly is loaded.")]
    internal static void RegisterCodecs()
    {
        RuntimeAssetCodecRegistry.Register<AnimationClip>((bytes, _) =>
            AnimationClipSerialization.Restore(YamlUtility.Deserialize<AnimationClipData>(TextAsset.Decode(bytes.ToArray()))));
        RuntimeAssetCodecRegistry.Register<AnimatorController>((bytes, _) =>
            AnimatorControllerSerialization.Restore(YamlUtility.Deserialize<AnimatorControllerData>(TextAsset.Decode(bytes.ToArray()))));
    }
}
