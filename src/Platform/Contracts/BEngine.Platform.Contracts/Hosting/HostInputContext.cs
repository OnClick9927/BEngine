namespace BEngine.Hosting;

public interface IHostInputContext : IDisposable
{
    void Poll();
    void ApplyCursorState();
}

public interface IHostInputContextFactory
{
    IHostInputContext Create(IHostWindow window);
}
