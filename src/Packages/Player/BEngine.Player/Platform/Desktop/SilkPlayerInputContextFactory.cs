#if !BENGINE_PLAYER_PORTABLE
namespace BEngine.Player;

public sealed class SilkPlayerInputContextFactory : IPlayerInputContextFactory
{
    public IPlayerInputContext Create(IPlayerWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window is not SilkPlayerWindow silkWindow)
            throw new ArgumentException(
                $"{nameof(SilkPlayerInputContextFactory)} requires a {nameof(SilkPlayerWindow)}.",
                nameof(window));
        return new SilkPlayerInputContext(silkWindow);
    }
}

#endif
