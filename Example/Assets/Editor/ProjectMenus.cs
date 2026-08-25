using BEngine;
using BEngine.Editor;

namespace Game.Editor;

public static class ProjectMenus
{
    [MenuItem("Tools/Showcase/Log Selected Object", false, 100)]
    private static void LogSelectedObject()
    {
        Debug.Log($"Selected: {Selection.activeGameObject?.name}");
    }

    [MenuItem("Tools/Showcase/Log Selected Object", true)]
    private static bool ValidateLogSelectedObject() => Selection.activeGameObject is not null;
}
