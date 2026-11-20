using System.Runtime.CompilerServices;
namespace BEngine.Editor;
internal static class DiagnosticIconAliases
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Register editor diagnostics icons.")]
    internal static void Register()
    {
        EditorIconRegistry.RegisterName("FrameDebugger", "Icons/Toolbar/FrameDebugger.png");
    }
}
