using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

/// <summary>One backend selection for content, the manifest and native Host linking.</summary>
public static class PlayerGraphicsBuildSettings
{
    public static IReadOnlyList<GraphicsBackend> GetSupported(BuildTargetDescriptor target,
        PlatformPlayerBuildCapabilities? capabilities = null)
    {
        var supported = capabilities is not null && capabilities.TryGetRenderer(target.TargetId, out var renderer)
            ? renderer.Backends : target.GraphicsBackends;
        return target.HasBuiltInPlayerHost ? supported.Where(backend => backend == GraphicsBackend.OpenGL ||
            backend == GraphicsBackend.Vulkan && target.Platform == BuildTargetPlatform.Windows).ToArray() : supported;
    }

    public static IReadOnlyList<GraphicsBackend> Resolve(PlayerBuildRequest request,
        BuildTargetDescriptor target, PlatformPlayerBuildCapabilities? capabilities = null)
    {
        var supported = GetSupported(target, capabilities);
        var selected = request.GraphicsBackends.Count == 0 ? supported : request.GraphicsBackends;
        if (selected.Count == 0 || selected.Any(backend => backend == GraphicsBackend.Auto || !supported.Contains(backend)) ||
            selected.Distinct().Count() != selected.Count)
            throw new InvalidDataException($"Invalid graphics backend selection for '{target.TargetId}'.");
        return supported.Where(selected.Contains).ToArray();
    }

    public static string DesktopVariant(IReadOnlyList<GraphicsBackend> backends) =>
        backends.Contains(GraphicsBackend.OpenGL) && backends.Contains(GraphicsBackend.Vulkan) ? "Both" :
        backends.Contains(GraphicsBackend.Vulkan) ? "Vulkan" : "OpenGL";
}
