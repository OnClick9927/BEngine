using BEngine.Editor;
using BEngine.Navigation2D;

namespace BEngine.Navigation2D.Editor;

internal static class Navigation2DEditorRegistration
{
    [InitializeOnLoadMethod]
    private static void RegisterIcons()
    {
        EditorIconRegistry.Register(typeof(NavigationAgent2D), "Navigation2D.png");
        EditorIconRegistry.Register(typeof(NavigationSurface2D), "Navigation2D.png");
        EditorIconRegistry.Register(typeof(NavigationObstacle2D), "Navigation2D.png");
    }
}
