namespace BEngine.ProjectSystem;

public sealed class ProjectData
{
    public string Format { get; set; } = "BEngine.Project";
    public int Version { get; set; } = 1;
    public string EngineVersion { get; set; } = "0.1.0";
    public string Name { get; set; } = "BEngine Game";
    public string StartupScene { get; set; } = "Assets/Scenes/Main.scene.yaml";
    public string AssetsDirectory { get; set; } = "Assets";
    public string ScriptsDirectory { get; set; } = "Assets/Scripts";
    public string EditorScriptsDirectory { get; set; } = "Assets/Editor";
    public string FixedDeltaTime { get; set; } = "0.02";
    public WindowData Window { get; set; } = new();
}
