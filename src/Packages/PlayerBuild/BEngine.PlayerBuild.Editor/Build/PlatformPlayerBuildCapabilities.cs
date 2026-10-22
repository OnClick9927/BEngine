using BEngine.Build;
using BEngine.Platform;
using BEngine.Rendering.Rhi;

namespace BEngine.Editor;

public sealed record PlatformPlayerRendererCapability(
    string ProviderId,
    string TargetId,
    IReadOnlyList<GraphicsBackend> Backends,
    string HostProjectPath,
    bool SupportsHybridAotInterpreter = false,
    string? InterpreterBridgeAssemblyName = null);

public sealed class PlatformPlayerBuildCapabilities
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PlatformPlayerRendererCapability> _renderers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly FileUtility _fileSystem;
    private readonly PathUtility _pathService;

    public PlatformPlayerBuildCapabilities(FileUtility fileSystem, PathUtility pathService)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
    }

    public void RegisterRenderer(PlatformPlayerRendererCapability capability, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability.ProviderId);
        var target = BEngine.Platform.BuildTargetUtility.Current.Get(capability.TargetId);
        if (target.HasBuiltInPlayerHost)
            throw new ArgumentException("Platform renderer capabilities are only required by non-desktop hosts.",
                nameof(capability));
        if (capability.Backends.Count == 0 ||
            capability.Backends.Any(backend => !target.GraphicsBackends.Contains(backend)))
            throw new ArgumentException(
                $"Renderer '{capability.ProviderId}' does not expose a backend allowed by '{target.TargetId}'.",
                nameof(capability));
        ArgumentException.ThrowIfNullOrWhiteSpace(capability.HostProjectPath);
        var hostProject = _pathService.GetFullPath(capability.HostProjectPath);
        if (!_fileSystem.FileExists(hostProject))
            throw new FileNotFoundException(
                $"Renderer '{capability.ProviderId}' Player Host project was not found.", hostProject);
        if (target.Platform == BuildTargetPlatform.IOS &&
            string.IsNullOrWhiteSpace(capability.InterpreterBridgeAssemblyName))
            throw new ArgumentException(
                "An iOS Player Host must name the small host assembly interpreted by Mono; Core assemblies remain AOT.",
                nameof(capability));
        lock (_gate)
        {
            if (_renderers.ContainsKey(target.TargetId) && !replace)
                throw new InvalidOperationException(
                    $"A platform renderer is already registered for '{target.TargetId}'.");
            _renderers[target.TargetId] = capability with
            {
                TargetId = target.TargetId,
                HostProjectPath = hostProject,
                Backends = target.GraphicsBackends.Where(capability.Backends.Contains).ToArray()
            };
        }
    }

    public bool UnregisterRenderer(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        lock (_gate) return _renderers.Remove(targetId);
    }

    public bool TryGetRenderer(string targetId, out PlatformPlayerRendererCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        lock (_gate) return _renderers.TryGetValue(targetId, out capability!);
    }
}
