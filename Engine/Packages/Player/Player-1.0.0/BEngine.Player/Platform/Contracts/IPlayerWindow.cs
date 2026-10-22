namespace BEngine.Player;

public interface IPlayerWindow : IDisposable
{
    event Action? Loaded;
    event Action<double>? Updating;
    event Action<double>? Rendering;
    event Action? Closing;
    event Action<bool>? FocusChanged;

    PlayerWindowSize Size { get; }
    PlayerWindowSize FramebufferSize { get; }
    bool IsFullscreen { get; }
    bool VSync { get; }

    void Run();
    void Close();
}
