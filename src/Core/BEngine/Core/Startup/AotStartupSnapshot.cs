namespace BEngine.Startup;

/// <summary>
/// Immutable state consumed by a project's AOT scene UI.
/// </summary>
public sealed record AotStartupSnapshot(
    AotStartupPhase Phase,
    string Status,
    float Progress,
    long CompletedBytes,
    long TotalBytes,
    bool CanCheckForUpdates,
    bool RequiresUpdateConfirmation,
    bool CanEnterGame,
    bool CanRetry,
    string TargetVersion = "",
    int UpdateBundleCount = 0,
    string Error = "",
    bool CanDeclineUpdate = false);
