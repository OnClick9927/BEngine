namespace BEngine.Player;

public interface IPlayerSplashWindow : IDisposable
{
    bool IsVisible { get; }
    void Pump();
}
