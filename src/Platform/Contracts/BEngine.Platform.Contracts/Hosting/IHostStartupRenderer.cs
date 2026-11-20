using BEngine.Rendering.Rhi;
namespace BEngine.Hosting;

public interface IHostStartupRenderer : IDisposable
{
    void Render(int width, int height, HostStartupProgress progress);
}

public static class HostStartupRenderers
{
    public static Func<IGraphicsDevice, IHostStartupRenderer>? Factory { get; set; }
    public static IHostStartupRenderer Create(IGraphicsDevice device) => Factory?.Invoke(device) ??
        throw new InvalidOperationException("No startup presentation provider was registered by the loaded modules.");
}
