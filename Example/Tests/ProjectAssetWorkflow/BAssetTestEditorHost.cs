using BEngine.Editor;
using BEngine.ProjectSystem;
using BEngine.ProjectSystem.Editor;
using ProjectAssetDatabase = BEngine.ProjectSystem.Editor.AssetDatabase;

namespace BEngine.ExampleTests.ProjectAssetWorkflow;

internal sealed class TestEditorHost : IEditorHost, IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly ProjectAssetDatabase _assets;
    private readonly EditorTaskScheduler _scheduler = new(1);
    private readonly Scene _scene = new("Host Scene");
    private readonly string _previousDataPath;

    internal TestEditorHost(ProjectWorkspace workspace, ProjectAssetDatabase assets)
    {
        _workspace = workspace;
        _assets = assets;
        _previousDataPath = Application.dataPath;
        Application.dataPath = workspace.AssetsPath;
    }

    public IEditorTaskScheduler TaskScheduler => _scheduler;
    public Scene ActiveScene => _scene;
    public IReadOnlyList<Scene> OpenScenes => [_scene];
    public BObject? ActiveObject { get; set; }
    public GameObject? ActiveGameObject { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsChangingPlayMode => false;
    public bool IsPaused { get; set; }
    public Tool CurrentTool { get; set; }
    public string? ActiveProjectAssetPath => null;
    public string? ActiveProjectFolderPath => "Assets";
    public string ProjectRootPath => _workspace.RootPath;
    public string AssetsRootPath => _workspace.AssetsPath;
    public PrefabStage? CurrentPrefabStage => null;

    public void MarkSceneDirty() { }
    public bool MarkSceneDirty(Scene scene) => true;
    public void FrameSelected() { }
    public bool SaveActiveScene() => true;
    public bool SaveScene(Scene scene) => true;
    public Scene? OpenScene(string scenePath, OpenSceneMode mode) => null;
    public bool CloseScene(Scene scene, bool removeScene) => true;
    public bool SetActiveScene(Scene scene) => true;
    public void ShowWindow(EditorWindow window) { }
    public void FocusWindow(EditorWindow window) { }
    public void CloseWindow(EditorWindow window) { }
    public void RepaintWindow(EditorWindow window) { }
    public void RepaintAllWindows() { }
    public bool ExecuteMenuItem(string itemName) => false;
    public void RevealProjectAsset(string assetPath, bool beginRename) { }
    public bool CanExecuteProjectAssetCommand(ProjectAssetCommand command) => false;
    public bool ExecuteProjectAssetCommand(ProjectAssetCommand command) => false;
    public bool CanExecuteGameObjectCommand(GameObjectCommand command, GameObject? target) => false;
    public bool ExecuteGameObjectCommand(GameObjectCommand command, GameObject? target) => false;
    public bool CanExecuteComponentCommand(ComponentCommand command) => false;
    public bool ExecuteComponentCommand(ComponentCommand command) => false;
    public bool CanExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera) => false;
    public bool ExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera) => false;
    public bool CanExecuteMainMenuCommand(MainMenuCommand command) => false;
    public bool ExecuteMainMenuCommand(MainMenuCommand command) => false;
    public bool IsMainMenuCommandChecked(MainMenuCommand command) => false;
    public void Exit(int exitCode) { }
    public EditorAssetRecord[] FindAssets(string search) => _assets.FindAssets(search).Select(ToRecord).ToArray();
    public EditorAssetRecord? GetAsset(string assetPath) => _assets.GetRecord(assetPath) is { } record
        ? ToRecord(record) : null;
    public EditorAssetRecord? GetAsset(Guid guid) => _assets.GetRecord(guid) is { } record
        ? ToRecord(record) : null;
    public void RefreshAssets() => _assets.Refresh();
    public void ImportAsset(string assetPath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_workspace.RootPath,
            assetPath.Replace('/', Path.DirectorySeparatorChar)));
        _assets.ImportAsset(fullPath);
    }
    public void RequestScriptCompilation() { }
    public string CreateAssetFolder(string parentFolder, string newFolderName) => string.Empty;
    public bool DeleteAsset(string assetPath) => false;
    public string MoveAsset(string oldPath, string newPath) => "Not supported";
    public bool OpenPrefabStage(string assetPath) => false;
    public bool SavePrefabStage() => false;
    public void ClosePrefabStage() { }

    public void Dispose()
    {
        _scene.Dispose();
        _scheduler.Dispose();
        Application.dataPath = _previousDataPath;
    }

    private static EditorAssetRecord ToRecord(AssetRecord record) => new(
        record.Guid, record.AssetPath, record.SourcePath, record.AssetType, record.IsDirectory);
}
