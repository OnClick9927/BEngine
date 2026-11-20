namespace BEngine.ProjectSystem;

/// <summary>Execution inputs shared by logic hosts and Players; no authoring directories or SDK settings.</summary>
public class RuntimeProjectData
{
    public string Name { get; set; } = "BEngine Game";
    public string StartupScene { get; set; } = "Assets/Scenes/Main.scene.yaml";
    public string AssetsDirectory { get; set; } = "Assets";
    public string FixedDeltaTime { get; set; } = "0.02";
    public WindowData Window { get; set; } = new();
}
