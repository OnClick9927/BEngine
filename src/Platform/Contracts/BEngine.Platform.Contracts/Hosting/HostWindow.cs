using BEngine.Rendering.Rhi;

namespace BEngine.Hosting;

public readonly record struct HostWindowSize(int Width, int Height);

public sealed record HostWindowOptions(string Title, int Width, int Height, bool VSync)
{
    public IReadOnlyList<GraphicsBackend> GraphicsBackends { get; init; } = [GraphicsBackend.Auto];
}

/// <summary>A native application's window and frame-loop mechanism.</summary>
public interface IHostWindow : IDisposable
{
    event Action? Loaded;
    event Action<double>? Updating;
    event Action<double>? Rendering;
    event Action? Closing;
    event Action<bool>? FocusChanged;
    HostWindowSize Size { get; }
    HostWindowSize FramebufferSize { get; }
    bool IsFullscreen { get; }
    bool VSync { get; }
    void Run();
    void Close();
}

public interface IHostWindowFactory
{
    IHostWindow Create(HostWindowOptions options);
}
