namespace BEngine.Editor;

public sealed class AboutBEngineWindowModel(string windowId) : EditorWindowModel(windowId)
{
    public string EngineVersion { get; internal set; } = string.Empty;
    public string FrameworkDescription { get; internal set; } = string.Empty;
    public string OperatingSystemDescription { get; internal set; } = string.Empty;
    public string OperatingSystemArchitecture { get; internal set; } = string.Empty;
    public string ProcessArchitecture { get; internal set; } = string.Empty;
    public string ProjectPath { get; internal set; } = string.Empty;
    public string? DocumentationPath { get; internal set; }
}
