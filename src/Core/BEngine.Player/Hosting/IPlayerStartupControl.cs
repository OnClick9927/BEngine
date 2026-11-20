namespace BEngine.Player;

/// <summary>Host-neutral observation of an optional content preparation workflow.</summary>
public readonly record struct PlayerStartupState(
    string Phase, string Status, string Error, bool CanAdvance, bool IsFailure);

/// <summary>Allows an embedding host to advance preparation without knowing its content or UI policy.</summary>
public interface IPlayerStartupControl
{
    PlayerStartupState Current { get; }
    void Advance(bool preferExistingContent = false);
}
