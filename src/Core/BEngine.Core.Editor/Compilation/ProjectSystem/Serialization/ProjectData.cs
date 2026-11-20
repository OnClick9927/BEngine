namespace BEngine.ProjectSystem;

public sealed class ProjectData : RuntimeProjectData
{
    public string Format { get; set; } = "BEngine.Project";
    public int Version { get; set; } = 1;
    public string EngineVersion { get; set; } = string.Empty;
    public string ScenesDirectory { get; set; } = "Assets/Scenes";
    public string ScriptsDirectory { get; set; } = "Assets/Scripts";
    public string EditorScriptsDirectory { get; set; } = "Assets/Editor";
    public RuntimeProjectData ToRuntime() => new()
    {
        Name = Name, StartupScene = StartupScene, AssetsDirectory = AssetsDirectory,
        FixedDeltaTime = FixedDeltaTime,
        Window = new() { Title = Window.Title, Width = Window.Width, Height = Window.Height, VSync = Window.VSync }
    };
}
