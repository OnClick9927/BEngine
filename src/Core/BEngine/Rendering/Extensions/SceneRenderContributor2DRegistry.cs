using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public static class SceneRenderContributor2DRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Func<IGraphicsDevice, ISceneRenderContributor2D>> Factories =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<IGraphicsDevice, Dictionary<string, ISceneRenderContributor2D>> Instances =
        new(ReferenceEqualityComparer.Instance);

    public static void Register(string id, Func<IGraphicsDevice, ISceneRenderContributor2D> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        lock (Sync) Factories[id] = factory;
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        lock (Sync)
        {
            var removed = Factories.Remove(id);
            foreach (var perDevice in Instances.Values)
                if (perDevice.Remove(id, out var contributor)) contributor.Dispose();
            return removed;
        }
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (Sync)
        {
            foreach (var id in Factories.Where(pair =>
                             pair.Value.Method.DeclaringType?.Assembly == assembly ||
                             pair.Value.Method.ReturnType.Assembly == assembly)
                         .Select(pair => pair.Key).ToArray())
            {
                Factories.Remove(id);
                foreach (var perDevice in Instances.Values)
                    if (perDevice.Remove(id, out var contributor)) contributor.Dispose();
            }
        }
    }

    internal static long Collect(IGraphicsDevice device, Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder)
    {
        lock (Sync)
        {
            foreach (var contributor in GetContributors(device))
            {
                if (!RuntimePackageState.IsEnabled(contributor.packageId)) continue;
                submissionOrder = contributor.Collect(
                    scene, camera, width, height, submissions, submissionOrder);
            }
        }
        return submissionOrder;
    }

    internal static bool TryRender(IGraphicsDevice device, RenderBatch2D batch, RenderCamera camera,
        int width, int height, GraphicsRect viewport)
    {
        lock (Sync)
        {
            foreach (var contributor in GetContributors(device))
            {
                if (!RuntimePackageState.IsEnabled(contributor.packageId) || !contributor.CanRender(batch))
                    continue;
                contributor.Render(batch, camera, width, height, viewport);
                return true;
            }
        }
        return false;
    }

    internal static void Release(IGraphicsDevice device)
    {
        lock (Sync)
        {
            if (!Instances.Remove(device, out var contributors)) return;
            foreach (var contributor in contributors.Values) contributor.Dispose();
        }
    }

    private static IEnumerable<ISceneRenderContributor2D> GetContributors(IGraphicsDevice device)
    {
        if (!Instances.TryGetValue(device, out var perDevice))
            Instances[device] = perDevice = new Dictionary<string, ISceneRenderContributor2D>(StringComparer.Ordinal);
        foreach (var (id, factory) in Factories.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!perDevice.TryGetValue(id, out var contributor))
                perDevice[id] = contributor = factory(device);
            yield return contributor;
        }
    }
}
