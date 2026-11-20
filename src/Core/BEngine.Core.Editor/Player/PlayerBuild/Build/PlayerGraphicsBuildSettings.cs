using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor.Build;

/// <summary>One backend selection for content, the manifest and native Host linking.</summary>
public static class PlayerGraphicsBuildSettings
{
    public static IReadOnlyList<GraphicsBackend> GetSupported(BuildTargetDescriptor target,
        PlatformPlayerBuildCapabilities? capabilities = null) =>
        PlatformGraphicsBuildPolicy.GetSupportedBackends(target, capabilities);

    public static IReadOnlyList<GraphicsBackend> Resolve(PlayerBuildRequest request,
        BuildTargetDescriptor target, PlatformPlayerBuildCapabilities? capabilities = null)
    {
        var supported = GetSupported(target, capabilities);
        var selected = request.GraphicsBackends.Count == 0 ? supported : request.GraphicsBackends;
        [global::BEngine.SkipLoopGuard]
        bool Query1175(global::System.Collections.Generic.IEnumerable<global::BEngine.Rendering.Rhi.GraphicsBackend> querySource1175)
        {
            ArgumentNullException.ThrowIfNull(querySource1175, "source");
            bool Matches1175(global::BEngine.Rendering.Rhi.GraphicsBackend backend) => backend == GraphicsBackend.Auto || !supported.Contains(backend);
            global::System.ReadOnlySpan<global::BEngine.Rendering.Rhi.GraphicsBackend> queryValues1175;
            if (querySource1175.GetType() == typeof(global::BEngine.Rendering.Rhi.GraphicsBackend[]))
                queryValues1175 = (global::BEngine.Rendering.Rhi.GraphicsBackend[])querySource1175;
            else if (querySource1175.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Rendering.Rhi.GraphicsBackend>))
                queryValues1175 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Rendering.Rhi.GraphicsBackend>)querySource1175);
            else
            {
                foreach (var backend in querySource1175)
                    if (Matches1175(backend))
                        return true;
                return false;
            }
        
            foreach (var backend in queryValues1175)
                if (Matches1175(backend))
                    return true;
            return false;
        }
        
        if (selected.Count == 0 || Query1175(selected) || (((global::System.Collections.Generic.IEnumerable<global::BEngine.Rendering.Rhi.GraphicsBackend>)(selected)) switch
        {
            global::BEngine.Rendering.Rhi.GraphicsBackend[] { Length: 0 } => 0,
            var distinctItems1279 => distinctItems1279.ToHashSet<global::BEngine.Rendering.Rhi.GraphicsBackend>().Count
        }) != selected.Count)
            throw new InvalidDataException($"Invalid graphics backend selection for '{target.TargetId}'.");
        return supported.Where(selected.Contains).ToArray();
    }

    public static string DesktopVariant(IReadOnlyList<GraphicsBackend> backends) =>
        PlatformGraphicsBuildPolicy.DesktopVariant(backends);
}
