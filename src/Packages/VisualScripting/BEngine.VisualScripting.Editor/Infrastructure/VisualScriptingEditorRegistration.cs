using System.Runtime.CompilerServices;
using BEngine.Editor;

namespace BEngine.VisualScripting.Editor;

internal static class VisualScriptingEditorRegistration
{
    [ModuleInitializer, InitializeOnLoadMethod]
    internal static void Register()
    {
        AssetTypeRegistry.Register<VisualScriptGraph>(".visualscript.yaml", "Visual Script",
            context => VisualScriptGraph.Load(context.SourcePath), EditorBuiltinIcons.Assets.Script);
        EditorIconRegistry.Register(typeof(VisualScriptRunner), EditorBuiltinIcons.Components.Script);
    }
}
