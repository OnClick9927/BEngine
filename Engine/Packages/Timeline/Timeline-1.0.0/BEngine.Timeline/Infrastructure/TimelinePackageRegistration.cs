using System.Runtime.CompilerServices;
namespace BEngine;

internal static class TimelinePackageRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register package codecs when its assembly is loaded.")]
    internal static void RegisterCodec()
    {
        RuntimeAssetCodecRegistry.Register<TimelineAsset>((bytes, _) =>
            TimelineAssetSerialization.Deserialize(TextAsset.Decode(bytes.ToArray())));
        PlayableDirector.RuntimeMethodPreparation.Prepare();
    }
}
