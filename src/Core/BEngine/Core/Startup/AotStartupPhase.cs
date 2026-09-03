namespace BEngine.Startup;

/// <summary>
/// Describes the visible AOT bootstrap stage that runs before hot-update code is activated.
/// </summary>
public enum AotStartupPhase
{
    Initializing,
    WaitingForUpdateCheck,
    CheckingForUpdates,
    AwaitingUpdateConfirmation,
    Downloading,
    Verifying,
    Activating,
    ValidatingInstalledContent,
    GameContentMissing,
    Ready,
    Failed
}
