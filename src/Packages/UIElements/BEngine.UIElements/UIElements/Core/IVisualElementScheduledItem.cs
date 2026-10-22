namespace BEngine.UIElements;

public interface IVisualElementScheduledItem : IDisposable
{
    bool isActive { get; }
    IVisualElementScheduledItem Every(TimeSpan interval);
    IVisualElementScheduledItem EveryFrames(int frameCount);
    void Pause();
    void Resume();
}
