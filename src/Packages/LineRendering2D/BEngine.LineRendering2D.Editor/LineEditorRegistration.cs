using System.Runtime.CompilerServices;

namespace BEngine.Editor;

internal static class LineEditorRegistration
{
    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Package editor icons.")]
    internal static void Register()
    {
        EditorIconRegistry.Register(typeof(LineRenderer2D), EditorBuiltinIcons.Components.SpriteRenderer);
        EditorIconRegistry.Register(typeof(TrailRenderer2D), EditorBuiltinIcons.Components.SpriteRenderer);
    }
}
