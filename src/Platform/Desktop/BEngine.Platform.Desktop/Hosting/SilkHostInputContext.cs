using BEngine.Hosting;
using BEngine.Platform;

namespace BEngine.Platform;

internal sealed class SilkHostInputContext : IHostInputContext
{
    private readonly SilkInputSession _session;

    internal SilkHostInputContext(SilkHostWindow window)
    {
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
