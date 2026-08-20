namespace BEngine.Launcher;

internal sealed class LauncherPackageInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Version { get; init; }
    public required string Description { get; init; }
    public required string DirectoryPath { get; init; }
    public bool HasRuntime { get; init; }
    public bool HasEditor { get; init; }
    public string SelectionLabel => $"{DisplayName}  {Version}";
}
