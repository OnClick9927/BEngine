
namespace BEngine.Editor;

public static class EditorSceneManager
{
    public static event Action<Scene, OpenSceneMode>? sceneOpened;
    public static event Action<Scene>? sceneClosed;
    public static event Action<Scene?, Scene>? activeSceneChangedInEditMode;

    public static Scene? activeScene => EditorBridge.Host?.ActiveScene;
    public static int sceneCount => EditorBridge.Host?.OpenScenes.Count ?? 0;

    public static void MarkSceneDirty() => EditorBridge.Host?.MarkSceneDirty();
    public static bool MarkSceneDirty(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return EditorBridge.Host?.MarkSceneDirty(scene) ?? false;
    }
    public static Scene? GetActiveScene() => activeScene;
    public static Scene GetSceneAt(int index) => EditorBridge.Host?.OpenScenes[index] ??
        throw new ArgumentOutOfRangeException(nameof(index));
    public static bool SetActiveScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return EditorBridge.Host?.SetActiveScene(scene) ?? false;
    }
    public static bool SaveOpenScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
        var host = EditorBridge.Host;
        if (host is null) return false;
        var saved = true;
        foreach (var scene in host.OpenScenes.Where(scene => scene.isLoaded)) saved &= host.SaveScene(scene);
        return saved;
    }
    public static bool SaveScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.IsRuntimeOnly) return false;
        return EditorBridge.Host?.SaveScene(scene) ?? false;
    }
    public static Scene? OpenScene(string scenePath, OpenSceneMode mode = OpenSceneMode.Single) =>
        EditorBridge.Host?.OpenScene(scenePath, mode);
    public static bool CloseScene(Scene scene, bool removeScene = true)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return EditorBridge.Host?.CloseScene(scene, removeScene) ?? false;
    }

    internal static void RaiseSceneOpened(Scene scene, OpenSceneMode mode) => sceneOpened?.Invoke(scene, mode);
    internal static void RaiseSceneClosed(Scene scene) => sceneClosed?.Invoke(scene);
    internal static void RaiseActiveSceneChanged(Scene? previous, Scene next) =>
        activeSceneChangedInEditMode?.Invoke(previous, next);
}
