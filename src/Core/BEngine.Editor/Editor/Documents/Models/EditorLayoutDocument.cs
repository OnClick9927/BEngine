using BEngine.Documents;

namespace BEngine.Editor.Documents;

public sealed class EditorLayoutDocument
{
    public string Format { get; set; } = "BEngine.EditorLayout";
    public int Version { get; set; } = 2;
    public string Name { get; set; } = "Last Session";
    public string ActiveLayout { get; set; } = "Last Session";
    public int WindowX { get; set; } = 60;
    public int WindowY { get; set; } = 40;
    public int WindowWidth { get; set; } = 1280;
    public int WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public float HierarchyWidth { get; set; } = 250f;
    public float InspectorWidth { get; set; } = 320f;
    public float BottomHeight { get; set; } = 190f;
    public float ProjectFoldersWidth { get; set; } = 240f;
    public float ProjectPackagesHeight { get; set; }
    public float ProjectThumbnailSize { get; set; } = 64f;
    public string ProjectBrowserMode { get; set; } = "OneColumn";
    public bool ShowHierarchy { get; set; } = true;
    public bool ShowInspector { get; set; } = true;
    public bool ShowBottomPanel { get; set; } = true;
    public bool ShowSceneView { get; set; } = true;
    public bool ShowGameView { get; set; } = true;
    public bool ShowProject { get; set; } = true;
    public bool ShowConsole { get; set; } = true;
    public string HierarchyDock { get; set; } = "Left";
    public string InspectorDock { get; set; } = "Right";
    public float SceneCameraPositionX { get; set; }
    public float SceneCameraPositionY { get; set; }
    public float SceneCameraRotation { get; set; }
    public float SceneCameraSize { get; set; } = 5f;
    public float SceneCameraBackgroundR { get; set; } = 0.055f;
    public float SceneCameraBackgroundG { get; set; } = 0.062f;
    public float SceneCameraBackgroundB { get; set; } = 0.071f;
    public bool SceneCameraDrawGrid { get; set; } = true;
    public string? MaximizedPanelId { get; set; }
    public string? FocusedWindowId { get; set; }
    public EditorDockNodeDocument? DockRoot { get; set; }
    public List<EditorWindowLayoutDocument> Windows { get; set; } = [];
    public List<EditorWindowLayoutDocument> ClosedWindows { get; set; } = [];
}
