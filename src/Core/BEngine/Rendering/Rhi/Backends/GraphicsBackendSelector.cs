using BEngine.Build;

namespace BEngine.Rendering.Rhi;

public sealed record GraphicsBackendSelection(
    GraphicsBackend Selected,
    IReadOnlyList<GraphicsBackend> Candidates,
    IReadOnlyDictionary<GraphicsBackend, string> Rejections);

public static class GraphicsBackendSelector
{
    public static IReadOnlyList<GraphicsBackend> GetCandidates(
        BuildTargetManifest target,
        GraphicsBackend preferred)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Validate();
        IEnumerable<GraphicsBackend> candidates = target.GraphicsBackends;
        if (preferred != GraphicsBackend.Auto)
            candidates = new[] { preferred }.Concat(candidates);
        return Array.AsReadOnly(candidates
            .Where(backend => backend != GraphicsBackend.Auto)
            .Distinct()
            .ToArray());
    }

    public static GraphicsBackendSelection Select(
        BuildTargetManifest target,
        GraphicsBackend preferred,
        Func<GraphicsBackend, GraphicsBackendSupport> querySupport)
    {
        ArgumentNullException.ThrowIfNull(querySupport);
        var candidates = GetCandidates(target, preferred);
        var rejected = new Dictionary<GraphicsBackend, string>();
        foreach (var backend in candidates)
        {
            var support = querySupport(backend);
            if (support.CanCreateDevice)
                return new GraphicsBackendSelection(backend, candidates, rejected);
            rejected[backend] = support.Reason;
        }
        throw new PlatformNotSupportedException(
            $"No graphics backend can create a device for build target '{target.TargetId}': " +
            string.Join("; ", rejected.Select(item => $"{item.Key}: {item.Value}")));
    }
}
