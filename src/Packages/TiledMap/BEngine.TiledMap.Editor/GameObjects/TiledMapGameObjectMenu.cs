using BEngine.Editor;

namespace BEngine.TiledMap.Editor;

internal static class TiledMapGameObjectMenu
{
    [MenuItem("GameObject/2D Object/Tilemap", false, 21)]
    private static void Create(MenuCommand command)
    {
        var parent = command.context switch
        {
            GameObject contextObject => contextObject,
            Component component => component.gameObject,
            _ => Selection.activeGameObject
        };
        var scene = parent?.scene ?? EditorSceneManager.activeScene;
        if (scene is null) return;
        var created = scene.CreateGameObject("Tilemap");
        created.AddComponent<Tilemap>();
        created.AddComponent<TilemapRenderer>();
        if (parent is not null) created.transform.SetParent(parent.transform, worldPositionStays: false);
        Undo.RegisterCreatedObjectUndo(created, "Create Tilemap");
        Selection.activeGameObject = created;
        EditorUtility.SetDirty(created);
        EditorApplication.RepaintHierarchyWindow();
    }

    [MenuItem("GameObject/2D Object/Tilemap", true)]
    private static bool ValidateCreate(MenuCommand command) => EditorSceneManager.activeScene is not null;
}
