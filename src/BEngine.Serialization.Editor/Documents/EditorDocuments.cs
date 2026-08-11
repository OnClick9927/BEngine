namespace BEngine.Serialization.Editor.Documents;

public sealed class EditorSettingsDocument
{
    public string Format { get; set; } = "BEngine.EditorSettings";
    public int Version { get; set; } = 1;
    public string Layout { get; set; } = "Unity";
    public string LastScene { get; set; } = "Assets/Scenes/Main.scene.yaml";
}

public sealed class EditorLayoutDocument
{
    public string Format { get; set; } = "BEngine.EditorLayout";
    public int Version { get; set; } = 1;
    public int WindowX { get; set; } = 60;
    public int WindowY { get; set; } = 40;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public float HierarchyWidth { get; set; } = 250f;
    public float InspectorWidth { get; set; } = 320f;
    public float BottomHeight { get; set; } = 190f;
    public float ProjectFoldersWidth { get; set; } = 210f;
    public string ProjectBrowserMode { get; set; } = "TwoColumn";
    public bool ShowHierarchy { get; set; } = true;
    public bool ShowInspector { get; set; } = true;
    public bool ShowBottomPanel { get; set; } = true;
    public bool ShowSceneView { get; set; } = true;
    public bool ShowGameView { get; set; } = true;
    public bool ShowProject { get; set; } = true;
    public bool ShowConsole { get; set; } = true;
    public string HierarchyDock { get; set; } = "Left";
    public string InspectorDock { get; set; } = "Right";
    public float SceneCameraPositionX { get; set; } = 5f;
    public float SceneCameraPositionY { get; set; } = 4f;
    public float SceneCameraPositionZ { get; set; } = -7f;
    public float SceneCameraPitch { get; set; } = 20f;
    public float SceneCameraYaw { get; set; } = -35f;
    public float SceneCameraFieldOfView { get; set; } = 60f;
    public float SceneCameraNearClipPlane { get; set; } = 0.05f;
    public float SceneCameraFarClipPlane { get; set; } = 2000f;
    public float SceneCameraMoveSpeed { get; set; } = 5f;
    public float SceneCameraFastMoveMultiplier { get; set; } = 2.4f;
    public float SceneCameraLookSensitivity { get; set; } = 0.2f;
    public float SceneCameraBackgroundR { get; set; } = 0.055f;
    public float SceneCameraBackgroundG { get; set; } = 0.062f;
    public float SceneCameraBackgroundB { get; set; } = 0.071f;
    public bool SceneCameraDrawSkybox { get; set; } = true;
    public bool SceneCameraDrawGrid { get; set; } = true;
}

public sealed class EditorPreferencesDocument
{
    public string Format { get; set; } = "BEngine.Preferences";
    public int Version { get; set; } = 1;
    public string Locale { get; set; } = "zh-CN";
    public string ExternalScriptEditor { get; set; } = string.Empty;
    public int EditorFontSize { get; set; } = 16;
    public bool AutoRefreshAssets { get; set; } = true;
    public bool ShowAssetMetaFiles { get; set; }
}

public sealed class LauncherSettingsDocument
{
    public string Format { get; set; } = "BEngine.LauncherSettings";
    public int Version { get; set; } = 1;
    public string LastProjectDirectory { get; set; } = string.Empty;
}
