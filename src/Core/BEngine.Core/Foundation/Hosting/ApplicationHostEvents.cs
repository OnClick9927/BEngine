namespace BEngine.Hosting;

/// <summary>
/// Entry points for an application's host to forward operating-system or editor
/// lifecycle notifications on the engine thread. These do not choose pause policy
/// or retain a host, window, service scope, or extension assembly.
/// </summary>
public static class ApplicationHostEvents
{
    public static void NotifyFocusChanged(bool focused) => Application.SetFocus(focused);
    public static void NotifyPauseChanged(bool paused) => Application.SetPaused(paused);
    public static void NotifyLowMemory() => Application.RaiseLowMemory();
}
