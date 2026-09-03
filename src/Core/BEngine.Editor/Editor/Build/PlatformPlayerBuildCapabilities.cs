using BEngine.Build;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed record PlatformPlayerRendererCapability(
    string ProviderId,
    string TargetId,
    IReadOnlyList<GraphicsBackend> Backends,
    string HostProjectPath,
    bool SupportsHybridAotInterpreter = false,
    string? InterpreterBridgeAssemblyName = null);

public static class PlatformPlayerBuildCapabilities
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PlatformPlayerRendererCapability> Renderers =
        new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterRenderer(PlatformPlayerRendererCapability capability, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability.ProviderId);
        var target = BuildTargetCatalog.Get(capability.TargetId);
        if (target.HasBuiltInPlayerHost)
            throw new ArgumentException("Platform renderer capabilities are only required by non-desktop hosts.",
                nameof(capability));
        if (capability.Backends.Count == 0 ||
            capability.Backends.Any(backend => !target.GraphicsBackends.Contains(backend)))
            throw new ArgumentException(
                $"Renderer '{capability.ProviderId}' does not expose a backend allowed by '{target.TargetId}'.",
                nameof(capability));
        ArgumentException.ThrowIfNullOrWhiteSpace(capability.HostProjectPath);
        var hostProject = Path.GetFullPath(capability.HostProjectPath);
        if (!File.Exists(hostProject))
            throw new FileNotFoundException(
                $"Renderer '{capability.ProviderId}' Player Host project was not found.", hostProject);
        if (target.Platform == BuildTargetPlatform.IOS &&
            string.IsNullOrWhiteSpace(capability.InterpreterBridgeAssemblyName))
            throw new ArgumentException(
                "An iOS Player Host must name the small host assembly interpreted by Mono; Core assemblies remain AOT.",
                nameof(capability));
        lock (Gate)
        {
            if (Renderers.ContainsKey(target.TargetId) && !replace)
                throw new InvalidOperationException(
                    $"A platform renderer is already registered for '{target.TargetId}'.");
            Renderers[target.TargetId] = capability with
            {
                TargetId = target.TargetId,
                HostProjectPath = hostProject,
                Backends = target.GraphicsBackends.Where(capability.Backends.Contains).ToArray()
            };
        }
    }

    public static bool UnregisterRenderer(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        lock (Gate) return Renderers.Remove(targetId);
    }

    public static bool TryGetRenderer(string targetId, out PlatformPlayerRendererCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        lock (Gate) return Renderers.TryGetValue(targetId, out capability!);
    }
}
