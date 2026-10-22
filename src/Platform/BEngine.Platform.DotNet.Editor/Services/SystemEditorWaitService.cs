namespace BEngine.Editor;

public sealed class SystemEditorWaitService : IEditorWaitService
{
    public void Wait(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));
        Thread.Sleep(duration);
    }
}
