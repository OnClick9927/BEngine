namespace BEngine.Startup;

/// <summary>
/// Player-owned startup service exposed to components in the project's AOT assembly.
/// </summary>
public interface IAotStartupFlow
{
    AotStartupSnapshot Current { get; }
    event Action<AotStartupSnapshot>? Changed;

    void CheckForUpdates();
    void ConfirmUpdate();
    void DeclineUpdate();
    void Retry();
    void EnterGame();
}
