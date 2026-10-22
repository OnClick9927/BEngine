using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public static class ScenePostProcessor2DRegistry
{
    private static readonly Dictionary<string, List<Registration>> Factories = new(StringComparer.Ordinal);
    private static readonly Dictionary<IGraphicsDevice, DeviceProcessors> Instances =
        new(ReferenceEqualityComparer.Instance);
    private static int _version;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string id, Func<IGraphicsDevice, IScenePostProcessor2D> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        var owner = factory.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly();
        if (!Factories.TryGetValue(id, out var registrations))
            Factories.Add(id, registrations = []);
        registrations.RemoveAll(item => item.Owner == owner);
        registrations.Add(new Registration(factory, owner));
        Invalidate(id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Factories.TryGetValue(id, out var registrations)) return false;
        var caller = Assembly.GetCallingAssembly();
        var index = registrations.FindLastIndex(item => item.Owner == caller);
        if (index < 0) index = registrations.Count - 1;
        var activeChanged = index == registrations.Count - 1;
        registrations.RemoveAt(index);
        if (registrations.Count == 0) Factories.Remove(id);
        if (activeChanged) Invalidate(id);
        return true;
    }

    internal static bool HasActive(IGraphicsDevice device, Scene scene, Camera2D camera)
    {
        // Preserve first-active short circuit and resolve again at Render's
        // original call site. No closure or LINQ iterator is needed per camera.
        foreach (var processor in GetProcessors(device))
            if (RuntimePackageState.IsEnabled(processor.packageId) && processor.IsActive(scene, camera))
                return true;
        return false;
    }

    internal static bool Render(IGraphicsDevice device, Scene scene, Camera2D camera,
        IGraphicsTexture2D source, GraphicsRect destination)
    {
        foreach (var processor in GetProcessors(device))
        {
            if (!RuntimePackageState.IsEnabled(processor.packageId) || !processor.IsActive(scene, camera))
                continue;
            processor.Render(scene, camera, source, destination);
            return true;
        }
        return false;
    }

    internal static void Release(IGraphicsDevice device)
    {
        if (!Instances.Remove(device, out var cache)) return;
        foreach (var processor in cache.ById.Values) processor.Dispose();
    }

    internal static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var (id, registrations) in Factories.ToArray())
        {
            var active = registrations.Count == 0 ? null : registrations[^1];
            registrations.RemoveAll(item => item.Owner == assembly);
            if (registrations.Count == 0) Factories.Remove(id);
            if (!ReferenceEquals(active, registrations.Count == 0 ? null : registrations[^1]))
                Invalidate(id);
        }
    }

    private static IScenePostProcessor2D[] GetProcessors(IGraphicsDevice device)
    {
        if (!Instances.TryGetValue(device, out var cache))
            Instances[device] = cache = new DeviceProcessors();
        if (cache.Version == _version) return cache.Ordered;
        return RebuildProcessors(device, cache);
    }

    // Keep captured rebuild predicates out of GetProcessors. Otherwise the C#
    // display class is allocated even when its cached array is returned early.
    private static IScenePostProcessor2D[] RebuildProcessors(IGraphicsDevice device, DeviceProcessors cache)
    {
        var activeFactories = Factories.Where(pair => pair.Value.Count > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var activeIds = activeFactories.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var stale in cache.ById.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            cache.ById[stale].Dispose();
            cache.ById.Remove(stale);
        }
        var ordered = new IScenePostProcessor2D[activeFactories.Length];
        for (var index = 0; index < activeFactories.Length; index++)
        {
            var id = activeFactories[index].Key;
            if (!cache.ById.TryGetValue(id, out var processor))
                cache.ById[id] = processor = activeFactories[index].Value[^1].Factory(device);
            ordered[index] = processor;
        }
        cache.Ordered = ordered;
        cache.Version = _version;
        return ordered;
    }

    private static void Invalidate(string id)
    {
        foreach (var cache in Instances.Values)
        {
            if (cache.ById.Remove(id, out var processor)) processor.Dispose();
            cache.Ordered = [];
            cache.Version = -1;
        }
        _version = unchecked(_version + 1);
    }

    private sealed record Registration(Func<IGraphicsDevice, IScenePostProcessor2D> Factory, Assembly Owner);

    private sealed class DeviceProcessors
    {
        internal readonly Dictionary<string, IScenePostProcessor2D> ById = new(StringComparer.Ordinal);
        internal IScenePostProcessor2D[] Ordered = [];
        internal int Version = -1;
    }
}
