using System.Runtime.CompilerServices;
using System.Text;

namespace BEngine;

internal static class ArtImportRuntimeRegistration
{
    [ModuleInitializer]
    internal static void Register() => RuntimeAssetCodecRegistry.Register<ArtDocument2D>((bytes, _) =>
        ArtDocument2D.Deserialize(Encoding.UTF8.GetString(bytes.Span)));
}
