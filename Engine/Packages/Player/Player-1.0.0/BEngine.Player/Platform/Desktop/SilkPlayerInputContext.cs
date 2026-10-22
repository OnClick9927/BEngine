#if !BENGINE_PLAYER_PORTABLE
using BEngine.Platform;

namespace BEngine.Player;

internal sealed class SilkPlayerInputContext : IPlayerInputContext
{
    private readonly SilkInputSession _session;
    private readonly SilkPlayerWindow _window;

    internal SilkPlayerInputContext(SilkPlayerWindow window)
    {
        _window = window;
        _session = new SilkInputSession(window.NativeWindow);
        // Escape is gameplay/UI input; explicit quit or native close ends the Player.
    }

    public void Poll() => _session.Poll();
    public void ApplyCursorState() => _session.ApplyCursorState();
    public void Dispose()
    {
        _session.Dispose();
    }
}
#endif
