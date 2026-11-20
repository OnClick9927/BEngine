namespace BEngine.Platform;

/// <summary>Android presentation limits. Applications can override them before creating the host surface.</summary>
public static class AndroidRuntimeSettings
{
    /// <summary>Zero uses the device's native resolution. UI and input retain framebuffer coordinates.</summary>
    // Keep 2K/120 Hz phones from submitting an unnecessarily large render
    // target. UI still uses the logical framebuffer coordinates and scales
    // through the normal Canvas path; this is a GPU/thermal safety limit.
    public static int maximumSurfaceDimension { get; set; } = 1280;
    public static int audioBufferMilliseconds { get; set; } = 240;
}
