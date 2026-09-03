namespace BEngine.Editor;

public sealed record PlayerContentUpdateResult(
    string OutputDirectory,
    string ContentVersion,
    TimeSpan Duration)
{
    public string LatestVersion { get; init; } = ContentVersion;
    public bool Promoted { get; init; } = true;
}
