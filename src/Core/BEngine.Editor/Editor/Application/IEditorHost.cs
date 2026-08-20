
namespace BEngine.Editor;

internal interface IEditorHost
{
    IEditorTaskScheduler TaskScheduler { get; }
    Scene ActiveScene { get; }
    IReadOnlyList<Scene> OpenScenes { get; }
    BObject? ActiveObject { get; set; }
    GameObject? ActiveGameObject { get; set; }
    void MarkSceneDirty();
    bool MarkSceneDirty(Scene scene);
    void FrameSelected();
    bool SaveActiveScene();
    bool SaveScene(Scene scene);
    Scene? OpenScene(string scenePath, OpenSceneMode mode);
    bool CloseScene(Scene scene, bool removeScene);
    bool SetActiveScene(Scene scene);
    bool IsPlaying { get; set; }
    bool IsPaused { get; set; }
    void ShowWindow(EditorWindow window);
    void CloseWindow(EditorWindow window);
    void RepaintWindow(EditorWindow window);
    void RepaintAllWindows();
    Tool CurrentTool { get; set; }
    bool ExecuteMenuItem(string itemName);
    string? ActiveProjectAssetPath { get; }
    string? ActiveProjectFolderPath { get; }
    void RevealProjectAsset(string assetPath, bool beginRename);
    bool CanExecuteProjectAssetCommand(ProjectAssetCommand command);
    bool ExecuteProjectAssetCommand(ProjectAssetCommand command);
    bool CanExecuteGameObjectCommand(GameObjectCommand command, GameObject? target);
    bool ExecuteGameObjectCommand(GameObjectCommand command, GameObject? target);
    bool CanExecuteComponentCommand(ComponentCommand command);
    bool ExecuteComponentCommand(ComponentCommand command);
    bool CanExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera);
    bool ExecuteCameraViewCommand(CameraViewCommand command, Camera2D camera);
    bool CanExecuteMainMenuCommand(MainMenuCommand command);
    bool ExecuteMainMenuCommand(MainMenuCommand command);
    bool IsMainMenuCommandChecked(MainMenuCommand command);
    void Exit(int exitCode);
    string ProjectRootPath { get; }
    string AssetsRootPath { get; }
    EditorAssetRecord[] FindAssets(string search);
    EditorAssetRecord? GetAsset(string assetPath);
    EditorAssetRecord? GetAsset(Guid guid);
    void RefreshAssets();
    void ImportAsset(string assetPath);
    void RequestScriptCompilation();
    string CreateAssetFolder(string parentFolder, string newFolderName);
    bool DeleteAsset(string assetPath);
    string MoveAsset(string oldPath, string newPath);
    PrefabStage? CurrentPrefabStage { get; }
    bool OpenPrefabStage(string assetPath);
    bool SavePrefabStage();
    void ClosePrefabStage();
}
