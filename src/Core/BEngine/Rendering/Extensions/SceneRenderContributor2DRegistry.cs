using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public static class SceneRenderContributor2DRegistry
{
    private static readonly Dictionary<string, Func<IGraphicsDevice, ISceneRenderContributor2D>> Factories =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<IGraphicsDevice, DeviceContributors> Instances =
        new(ReferenceEqualityComparer.Instance);
    private static KeyValuePair<string, Func<IGraphicsDevice, ISceneRenderContributor2D>>[] _orderedFactories = [];
    private static int _factoryVersion;
    private static int _orderedFactoryVersion = -1;

    public static void Register(string id, Func<IGraphicsDevice, ISceneRenderContributor2D> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        if (Factories.ContainsKey(id))
            foreach (var cache in Instances.Values)
                if (cache.ById.Remove(id, out var contributor)) contributor.Dispose();
        Factories[id] = factory;
        InvalidateFactoryOrder();
    }

    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var removed = Factories.Remove(id);
        foreach (var cache in Instances.Values)
        {
            if (cache.ById.Remove(id, out var contributor)) contributor.Dispose();
        }
        if (removed) InvalidateFactoryOrder();
        return removed;
    }

    internal static void UnregisterAssembly(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var ids = Factories.Where(pair =>
                         pair.Value.Method.DeclaringType?.Assembly == assembly ||
                         pair.Value.Method.ReturnType.Assembly == assembly)
                     .Select(pair => pair.Key).ToArray();
        foreach (var id in ids)
        {
            Factories.Remove(id);
            foreach (var cache in Instances.Values)
                if (cache.ById.Remove(id, out var contributor)) contributor.Dispose();
        }
        if (ids.Length > 0) InvalidateFactoryOrder();
    }

    internal static long Collect(IGraphicsDevice device, Scene scene, RenderCamera camera, int width, int height,
        ICollection<RenderSubmission2D> submissions, long submissionOrder, bool drawUi,
        Predicate<GameObject>? objectFilter)
    {
        foreach (var contributor in GetContributors(device))
        {
            if (!RuntimePackageState.IsEnabled(contributor.packageId) ||
                (!drawUi && contributor.rendersUi)) continue;
            submissionOrder = contributor.Collect(
                scene, camera, width, height, submissions, submissionOrder, objectFilter);
        }
        return submissionOrder;
    }

    internal static bool TryRender(IGraphicsDevice device, RenderBatch2D batch, RenderCamera camera,
        int width, int height, GraphicsRect viewport)
    {
        foreach (var contributor in GetContributors(device))
        {
            if (!RuntimePackageState.IsEnabled(contributor.packageId) || !contributor.CanRender(batch))
                continue;
            contributor.Render(batch, camera, width, height, viewport);
            return true;
        }
        return false;
    }

    internal static void Release(IGraphicsDevice device)
    {
        if (!Instances.Remove(device, out var contributors)) return;
        foreach (var contributor in contributors.ById.Values) contributor.Dispose();
    }

    private static ISceneRenderContributor2D[] GetContributors(IGraphicsDevice device)
    {
        if (!Instances.TryGetValue(device, out var cache))
            Instances[device] = cache = new DeviceContributors();
        if (cache.Version == _factoryVersion) return cache.Ordered;
        if (_orderedFactoryVersion != _factoryVersion)
        {
            _orderedFactories = Factories.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
            _orderedFactoryVersion = _factoryVersion;
        }

        var ordered = new ISceneRenderContributor2D[_orderedFactories.Length];
        for (var index = 0; index < _orderedFactories.Length; index++)
        {
            var (id, factory) = _orderedFactories[index];
            if (!cache.ById.TryGetValue(id, out var contributor))
                cache.ById[id] = contributor = factory(device);
            ordered[index] = contributor;
        }
        cache.Ordered = ordered;
        cache.Version = _factoryVersion;
        return ordered;
    }

    private static void InvalidateFactoryOrder() => _factoryVersion = unchecked(_factoryVersion + 1);

    private sealed class DeviceContributors
    {
        internal readonly Dictionary<string, ISceneRenderContributor2D> ById = new(StringComparer.Ordinal);
        internal ISceneRenderContributor2D[] Ordered = [];
        internal int Version = -1;
    }
}
