using BEngine.Rendering.Rhi;
using Silk.NET.Windowing;

namespace BEngine.Platform;

public static class SilkWindowFactory
{
    public static IReadOnlyList<GraphicsBackend> CompiledBackends { get; } =
    [
#if BENGINE_DESKTOP_VULKAN
        GraphicsBackend.Vulkan,
#endif
#if BENGINE_DESKTOP_OPENGL
        GraphicsBackend.OpenGL,
#endif
    ];

    public static (IWindow Window, SilkGraphicsSession Graphics) CreateVerified(
        WindowOptions options, IReadOnlyList<GraphicsBackend> candidates,
        Action<SilkGraphicsSession>? initialize = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        // SDL supplies Cocoa and IBus/Fcitx composition events and candidate positioning.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Silk.NET.Windowing.Sdl.SdlWindowing.Use();
            Silk.NET.Input.Sdl.SdlInput.RegisterPlatform();
        }
        else if (OperatingSystem.IsWindows())
        {
            // Reflection-only platform discovery is invisible to the managed trimmer.
            Silk.NET.Windowing.Glfw.GlfwWindowing.Use();
            Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();
        }
        var failures = new List<string>();
        // An explicit manifest is an allowlist. Only Auto/empty uses the compiled defaults.
        var ordered = candidates.Count == 0 || candidates.Contains(GraphicsBackend.Auto)
            ? candidates.Concat(CompiledBackends).Distinct() : candidates.Distinct();
        foreach (var backend in ordered)
        {
            if (!CompiledBackends.Contains(backend) || backend == GraphicsBackend.Vulkan && !OperatingSystem.IsWindows()) continue;
            IWindow? window = null;
            SilkGraphicsSession? session = null;
            try
            {
#if BENGINE_DESKTOP_VULKAN
                if (backend == GraphicsBackend.Vulkan && !Veldrid.GraphicsDevice.IsBackendSupported(Veldrid.GraphicsBackend.Vulkan))
                    throw new PlatformNotSupportedException("No Vulkan loader is available.");
#endif
                var candidate = options;
                candidate.IsVisible = false;
                candidate.ShouldSwapAutomatically = false;
                candidate.VSync = options.VSync;
                candidate.API = backend == GraphicsBackend.Vulkan ? GraphicsAPI.None :
                    new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));
                window = Window.Create(candidate);
                window.Initialize();
                if (window.GLContext is { IsCurrent: false } context) context.MakeCurrent();
                session = new SilkGraphicsSession(window, backend);
                DesktopGraphicsProbe.Verify(session);
                initialize?.Invoke(session);
                // Probing another window/device can change the published capabilities.
                GraphicsUtility.Current.ReportActive(session.Device.Capabilities);
                Debug.Log($"Automatic graphics selection: {backend} ({session.Device.Capabilities.DeviceName}); pixel verification passed.");
                if (failures.Count > 0) Debug.LogWarning("Graphics backend fallback: " + string.Join("; ", failures));
                return (window, session);
            }
            catch (Exception exception)
            {
                failures.Add($"{backend}: {exception.Message}");
                try { session?.Dispose(); }
                catch (Exception cleanup) { failures.Add($"{backend} device cleanup: {cleanup.Message}"); }
                try { window?.Dispose(); }
                catch (Exception cleanup) { failures.Add($"{backend} window cleanup: {cleanup.Message}"); }
            }
        }
        throw new PlatformNotSupportedException("No desktop renderer passed initialization and pixel verification: " + string.Join("; ", failures));
    }

}
