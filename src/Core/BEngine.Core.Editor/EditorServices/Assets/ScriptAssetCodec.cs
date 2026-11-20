using System.Runtime.CompilerServices;
using System.Text;

namespace BEngine.Editor;

internal static class ScriptAssetCodec
{
    [ModuleInitializer]
    internal static void Register() => RuntimeAssetCodecRegistry.Register<Script>(static (bytes, path) =>
    {
        var script = new Script { name = RuntimeServices.GetRequiredService<global::BEngine.Platform.PathUtility>().GetFileName(path) };
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        script.SetImportedContents(reader.ReadToEnd(), null);
        return script;
    });
}
