namespace BEngine.Player;

public interface IPlayerInputContext : IDisposable
{
    void Poll();
    void ApplyCursorState();
}
