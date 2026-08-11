
namespace BEngine.Editor;

internal interface IEditorHost
{
    Scene ActiveScene { get; }
    BObject? ActiveObject { get; set; }
    GameObject? ActiveGameObject { get; set; }
    void MarkSceneDirty();
    void FrameSelected();
    bool SaveActiveScene();
    bool OpenScene(string scenePath);
    bool IsPlaying { get; set; }
    bool IsPaused { get; set; }
    void ShowWindow(EditorWindow window);
    void CloseWindow(EditorWindow window);
    void RepaintWindow(EditorWindow window);
    void RepaintAllWindows();
    Tool CurrentTool { get; set; }
    bool ExecuteMenuItem(string itemName);
    void Exit(int exitCode);
    string ProjectRootPath { get; }
    string AssetsRootPath { get; }
    EditorAssetRecord[] FindAssets(string search);
    EditorAssetRecord? GetAsset(string assetPath);
    EditorAssetRecord? GetAsset(Guid guid);
    void RefreshAssets();
    void ImportAsset(string assetPath);
    string CreateAssetFolder(string parentFolder, string newFolderName);
    bool DeleteAsset(string assetPath);
    string MoveAsset(string oldPath, string newPath);
}

internal readonly record struct EditorAssetRecord(
    Guid Guid,
    string AssetPath,
    string SourcePath,
    string AssetType,
    bool IsDirectory);

internal static class EditorBridge
{
    internal static IEditorHost? Host { get; private set; }

    internal static void Attach(IEditorHost host) => Host = host;

    internal static void Detach(IEditorHost host)
    {
        if (ReferenceEquals(Host, host)) Host = null;
    }
}

public static class Selection
{
    private static Guid? _lastSelection;

    public static event Action? selectionChanged;

    public static BObject? activeObject
    {
        get => EditorBridge.Host?.ActiveObject;
        set
        {
            if (EditorBridge.Host is { } host)
            {
                host.ActiveObject = value;
                NotifyHostSelectionChanged(value);
            }
        }
    }

    public static GameObject? activeGameObject
    {
        get => EditorBridge.Host?.ActiveGameObject;
        set
        {
            if (EditorBridge.Host is { } host)
            {
                host.ActiveGameObject = value;
                NotifyHostSelectionChanged(value);
            }
        }
    }

    public static Transform? activeTransform => activeGameObject?.transform;
    public static BObject[] objects
    {
        get => activeObject is { } selected ? [selected] : [];
        set => activeObject = value?.FirstOrDefault();
    }
    public static GameObject[] gameObjects => activeGameObject is { } selected ? [selected] : [];
    public static Transform[] transforms => activeTransform is { } selected ? [selected] : [];
    public static int count => activeObject is null ? 0 : 1;
    public static int activeInstanceID => activeObject?.GetInstanceID() ?? 0;
    public static int[] instanceIDs
    {
        get => objects.Select(item => item.GetInstanceID()).ToArray();
        set => activeObject = value?.Select(BObject.FindObjectFromInstanceID).FirstOrDefault(item => item is not null);
    }

    public static bool Contains(BObject? target) => target is not null && ReferenceEquals(activeObject, target);
    public static bool Contains(int instanceId) => activeObject?.GetInstanceID() == instanceId;
    public static T[] GetFiltered<T>(SelectionMode mode = SelectionMode.Unfiltered) where T : BObject =>
        objects.OfType<T>().ToArray();

    internal static void NotifyHostSelectionChanged(BObject? selected)
    {
        var id = selected?.Id;
        if (_lastSelection == id) return;
        _lastSelection = id;
        selectionChanged?.Invoke();
        EditorWindow.NotifySelectionChanged();
    }
}

[Flags]
public enum SelectionMode
{
    Unfiltered = 0,
    TopLevel = 1,
    Deep = 2,
    ExcludePrefab = 4,
    Editable = 8,
    Assets = 16,
    DeepAssets = 32
}

public static class EditorSceneManager
{
    public static Scene? activeScene => EditorBridge.Host?.ActiveScene;
    public static int sceneCount => activeScene is null ? 0 : 1;

    public static void MarkSceneDirty() => EditorBridge.Host?.MarkSceneDirty();
    public static bool MarkSceneDirty(Scene scene)
    {
        if (!ReferenceEquals(scene, activeScene)) return false;
        MarkSceneDirty();
        return true;
    }
    public static Scene? GetActiveScene() => activeScene;
    public static Scene GetSceneAt(int index) => index == 0 && activeScene is { } scene
        ? scene
        : throw new ArgumentOutOfRangeException(nameof(index));
    public static bool SaveOpenScenes() => EditorBridge.Host?.SaveActiveScene() ?? false;
    public static bool SaveScene(Scene scene) => ReferenceEquals(scene, activeScene) && SaveOpenScenes();
    public static Scene? OpenScene(string scenePath) => EditorBridge.Host?.OpenScene(scenePath) is true
        ? activeScene
        : null;
}

public static class SceneView
{
    public static void FrameSelected() => EditorBridge.Host?.FrameSelected();
    public static void FrameLastActiveSceneView() => FrameSelected();
    public static void RepaintAll() => EditorBridge.Host?.RepaintAllWindows();
}

public enum Tool
{
    View,
    Move,
    Rotate,
    Scale,
    Rect,
    Transform,
    None
}

public enum PivotMode
{
    Center,
    Pivot
}

public enum PivotRotation
{
    Local,
    Global
}

public static class Tools
{
    private static Tool _fallbackTool = Tool.Move;

    public static Tool current
    {
        get => EditorBridge.Host?.CurrentTool ?? _fallbackTool;
        set
        {
            _fallbackTool = value;
            if (EditorBridge.Host is { } host) host.CurrentTool = value;
        }
    }

    public static PivotMode pivotMode { get; set; } = PivotMode.Pivot;
    public static PivotRotation pivotRotation { get; set; } = PivotRotation.Global;
    public static bool hidden { get; set; }
    public static Vector3 handlePosition => Selection.activeTransform?.position ?? Vector3.zero;
}

public static class EditorSnapSettings
{
    public static Fix64 move { get; set; } = (Fix64)0.5m;
    public static Fix64 rotate { get; set; } = (Fix64)15;
    public static Fix64 scale { get; set; } = (Fix64)0.1m;
}
