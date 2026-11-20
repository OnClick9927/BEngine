namespace BEngine.App.Startup;

/// <summary>
/// Describes the visible AOT bootstrap stage that runs before hot-update code is activated.
/// </summary>
[RuntimeTypeAlias("BEngine.Player.Startup.AotStartupPhase, BEngine.Player")]
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
