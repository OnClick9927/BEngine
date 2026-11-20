using BEngine.Build;

namespace BEngine.Rendering.Rhi;

public sealed record GraphicsBackendSelection(
    GraphicsBackend Selected,
    IReadOnlyList<GraphicsBackend> Candidates,
    IReadOnlyDictionary<GraphicsBackend, string> Rejections);

public static class GraphicsBackendSelector
{
    public static IReadOnlyList<GraphicsBackend> GetCandidates(
        BuildTargetDescriptor target,
        GraphicsBackend preferred)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.GraphicsBackends.Count == 0 || Query533(target.GraphicsBackends))
            throw new ArgumentException("A presentation target requires concrete graphics backends.", nameof(target));
        [global::BEngine.SkipLoopGuard]
        bool Query533(global::System.Collections.Generic.IEnumerable<global::BEngine.Rendering.Rhi.GraphicsBackend> querySource533)
        {
            ArgumentNullException.ThrowIfNull(querySource533, "source");
            bool Matches533(global::BEngine.Rendering.Rhi.GraphicsBackend backend) => backend == GraphicsBackend.Auto;
            global::System.ReadOnlySpan<global::BEngine.Rendering.Rhi.GraphicsBackend> queryValues533;
            if (querySource533.GetType() == typeof(global::BEngine.Rendering.Rhi.GraphicsBackend[]))
                queryValues533 = (global::BEngine.Rendering.Rhi.GraphicsBackend[])querySource533;
            else if (querySource533.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Rendering.Rhi.GraphicsBackend>))
                queryValues533 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Rendering.Rhi.GraphicsBackend>)querySource533);
            else
            {
                foreach (var backend in querySource533)
                    if (Matches533(backend))
                        return true;
                return false;
            }
        
            foreach (var backend in queryValues533)
                if (Matches533(backend))
                    return true;
            return false;
        }
        IEnumerable<GraphicsBackend> candidates = target.GraphicsBackends;
        if (preferred != GraphicsBackend.Auto && target.GraphicsBackends.Contains(preferred))
            candidates = new[] { preferred }.Concat(candidates);
        return Array.AsReadOnly((((global::System.Collections.Generic.IEnumerable<global::BEngine.Rendering.Rhi.GraphicsBackend>)(candidates.Where(backend => backend != GraphicsBackend.Auto))) switch
        {
            global::BEngine.Rendering.Rhi.GraphicsBackend[] { Length: 0 } => global::System.Array.Empty<global::BEngine.Rendering.Rhi.GraphicsBackend>(),
            var distinctItems998 => distinctItems998.ToHashSet<global::BEngine.Rendering.Rhi.GraphicsBackend>().ToArray()}));
    }

    public static GraphicsBackendSelection Select(
        BuildTargetDescriptor target,
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
