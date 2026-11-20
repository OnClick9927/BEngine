
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
    bool IsChangingPlayMode { get; }
    bool IsPaused { get; set; }
    void Step();
    void ShowWindow(IEditorView window);
    void FocusWindow(IEditorView window);
    void CloseWindow(IEditorView window);
    void RepaintWindow(IEditorView window);
    void RepaintAllWindows();
    Tool CurrentTool { get; set; }
    bool ExecuteMenuItem(string itemName);
    string? ActiveProjectAssetPath { get; }
    string? ActiveProjectFolderPath { get; }
    void RevealProjectAsset(string assetPath, bool beginRename);
    bool CanExecuteCommand(EditorCommandId command, BObject? target);
    bool ExecuteCommand(EditorCommandId command, BObject? target);
    bool IsCommandChecked(EditorCommandId command);
    void Exit(int exitCode);
    string ProjectRootPath { get; }
    string AssetsRootPath { get; }
    EditorAssetInfo[] FindAssets(string search);
    EditorAssetInfo? GetAsset(string assetPath);
    EditorAssetInfo? GetAsset(Guid guid);
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
