namespace BEngine.Launcher;

internal sealed class LauncherEngineInfo
{
    public required string Version { get; init; }
    public required string DirectoryPath { get; init; }
    public required string Description { get; init; }
    public string DisplayName => $"BEngine {Version}";
    public string DocumentationPath => Path.Combine(DirectoryPath, "Editor", "Doc", "index.html");
}
