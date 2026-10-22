namespace BEngine.Platform;

/// <summary>Optional host integration for touch contacts on a native window.</summary>
public interface ITouchInputService
{
    /// <summary>Call on the window's owner thread; dispose before destroying the window.</summary>
    ITouchInputSource? Attach(IntPtr window, bool suppressEmulatedMouse = true);
}

public interface ITouchInputSource : IDisposable
{
    /// <summary>Returns one frame in framebuffer pixels, with its origin at the bottom left.
    /// Call once per input frame, on the window's owner thread. Ended/canceled contacts appear once.</summary>
    Touch[] Poll(int framebufferWidth, int framebufferHeight, Fix64 deltaTime);
}
