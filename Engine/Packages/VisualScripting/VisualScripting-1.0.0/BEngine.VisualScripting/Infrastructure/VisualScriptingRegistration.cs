using System.Runtime.CompilerServices;
using System.Text;

namespace BEngine.VisualScripting;

internal static class VisualScriptingRegistration
{
    [ModuleInitializer]
    internal static void Register() => RuntimeAssetCodecRegistry.Register<VisualScriptGraph>((bytes, _) =>
        VisualScriptGraph.Deserialize(Encoding.UTF8.GetString(bytes.Span)));
}
