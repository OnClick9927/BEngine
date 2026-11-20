using BEngine.Hosting;
namespace BEngine.Platform;

public sealed class SilkHostInputContextFactory : IHostInputContextFactory
{
    public IHostInputContext Create(IHostWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window is not SilkHostWindow silkWindow)
            throw new ArgumentException(
                $"{nameof(SilkHostInputContextFactory)} requires a {nameof(SilkHostWindow)}.",
                nameof(window));
        return new SilkHostInputContext(silkWindow);
    }
}

