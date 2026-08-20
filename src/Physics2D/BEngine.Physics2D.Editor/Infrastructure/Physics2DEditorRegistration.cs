using BEngine.Editor;
using BEngine.Physics2D;

namespace BEngine.Physics2D.Editor;

internal static class Physics2DEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void RegisterIcons()
    {
        EditorIconRegistry.Register(typeof(Rigidbody2D), "Physics2D.png");
        EditorIconRegistry.Register(typeof(Collider2D), "Physics2D.png", useForChildren: true);
    }
}
