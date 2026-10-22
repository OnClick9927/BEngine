namespace BEngine.Editor.Diagnostics;

public interface IFrameDebuggerPlayMode
{
    bool IsPlaying { get; }
    bool IsPaused { get; set; }
}
