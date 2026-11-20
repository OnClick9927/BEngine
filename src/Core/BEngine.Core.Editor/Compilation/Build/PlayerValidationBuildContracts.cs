using BEngine.Build;

namespace BEngine.Editor;

/// <summary>Output locations and target chosen by an automated Player validation client.</summary>
public sealed record PlayerValidationBuildRequest(string ProjectPath, string OutputDirectory,
    string ContentOutputDirectory, string? TargetId = null);

public sealed record PlayerValidationBuildProgress(string Stage, string Message, float Progress);

/// <summary>Startup inputs supplied by the selected Player workflow, independent of validation protocols.</summary>
public sealed class PlayerValidationLaunchOptions
{
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } =
        new Dictionary<string, string>();
    public string ContentUrlEnvironmentVariable { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> BrowserQueryParameters { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, bool> AndroidBooleanExtras { get; init; } =
        new Dictionary<string, bool>();
}

/// <summary>A runnable Player and its prepared content, without workflow-specific settings or result types.</summary>
public sealed record PlayerValidationBuild(BuildTargetDescriptor Target, string OutputDirectory,
    string ContentDirectory)
{
    public string ExecutablePath { get; init; } = string.Empty;
    public string AndroidApplicationIdentifier { get; init; } = "com.example.game";
    public PlayerValidationLaunchOptions LaunchOptions { get; init; } = new();
}

/// <summary>Optional workflow for producing a Player that an automated validation client can launch.</summary>
public interface IPlayerValidationBuildProvider
{
    Task<PlayerValidationBuild> BuildAsync(PlayerValidationBuildRequest request,
        IProgress<PlayerValidationBuildProgress>? progress = null, CancellationToken cancellationToken = default);
}
