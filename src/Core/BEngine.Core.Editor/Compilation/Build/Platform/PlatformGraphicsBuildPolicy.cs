using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>Host linking and shader compiler requirements of the built-in graphics backends.</summary>
public static class PlatformGraphicsBuildPolicy
{
    private static readonly IReadOnlyList<GraphicsBackend> VulkanOffline =
        Array.AsReadOnly(new[] { GraphicsBackend.Vulkan });

    public static IReadOnlyList<GraphicsBackend> GetSupportedBackends(BuildTargetDescriptor target,
        PlatformPlayerBuildCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        var supported = capabilities is not null && capabilities.TryGetRenderer(target.TargetId, out var renderer)
            ? renderer.Backends : target.GraphicsBackends;
        return target.HasBuiltInPlayerHost ? supported.Where(backend => backend == GraphicsBackend.OpenGL ||
            backend == GraphicsBackend.Vulkan && target.Platform == BuildTargetPlatform.Windows).ToArray() : supported;
    }

    public static string DesktopVariant(IReadOnlyList<GraphicsBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        return backends.Contains(GraphicsBackend.OpenGL) && backends.Contains(GraphicsBackend.Vulkan) ? "Both" :
            backends.Contains(GraphicsBackend.Vulkan) ? "Vulkan" : "OpenGL";
    }

    /// <summary>
    /// The platform's Vulkan backend accepts precompiled SPIR-V. Other built-in backends
    /// consume their source format. This selection is independent of tool availability:
    /// a requested offline build must fail if its compiler is unavailable.
    /// </summary>
    public static IReadOnlyList<GraphicsBackend> GetOfflineBackends(
        IReadOnlyCollection<GraphicsBackend> backends, bool compileShaders)
    {
        ArgumentNullException.ThrowIfNull(backends);
        return compileShaders && backends.Contains(GraphicsBackend.Vulkan)
            ? VulkanOffline : Array.Empty<GraphicsBackend>();
    }

    public static bool IncludesRuntimeCompiler(
        IReadOnlyCollection<GraphicsBackend> backends, bool runtimeShaderCompilation)
    {
        ArgumentNullException.ThrowIfNull(backends);
        return runtimeShaderCompilation && backends.Contains(GraphicsBackend.Vulkan);
    }

    public static void Validate(IReadOnlyCollection<GraphicsBackend> backends,
        bool compileShaders, bool runtimeShaderCompilation)
    {
        ArgumentNullException.ThrowIfNull(backends);
        if (!compileShaders && !runtimeShaderCompilation && backends.Contains(GraphicsBackend.Vulkan))
            throw new InvalidDataException("Vulkan without a runtime compiler requires Compile Shaders.");
    }
}
