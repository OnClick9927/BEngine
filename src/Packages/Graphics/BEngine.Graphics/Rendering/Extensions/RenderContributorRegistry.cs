using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Rendering.Rhi;

namespace BEngine.Rendering;

public static class RenderContributorRegistry
{
    private static readonly Dictionary<string, List<Registration>> Factories =
        new(StringComparer.Ordinal);
    private static readonly Dictionary<IGraphicsDevice, DeviceContributors> Instances =
        new(ReferenceEqualityComparer.Instance);
    private static KeyValuePair<string, Func<IGraphicsDevice, IRenderContributor>>[] _orderedFactories = [];
    private static int _factoryVersion;
    private static int _orderedFactoryVersion = -1;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Register(string id, Func<IGraphicsDevice, IRenderContributor> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(factory);
        var owner = factory.Method.DeclaringType?.Assembly ?? Assembly.GetCallingAssembly();
        if (!Factories.TryGetValue(id, out var registrations))
            Factories.Add(id, registrations = []);
        registrations.RemoveAll(registration => registration.Owner == owner);
        registrations.Add(new Registration(factory, owner));
        ActiveFactoryChanged(id);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Unregister(string id) => UnregisterOwned(id, Assembly.GetCallingAssembly());

    internal static bool UnregisterOwned(string id, Assembly caller)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (!Factories.TryGetValue(id, out var registrations)) return false;
        var index = registrations.FindLastIndex(registration => registration.Owner == caller);
        if (index < 0) index = registrations.Count - 1;
        var activeChanged = index == registrations.Count - 1;
        registrations.RemoveAt(index);
        if (registrations.Count == 0) Factories.Remove(id);
        if (activeChanged) ActiveFactoryChanged(id);
        return true;
    }

    public static void UnregisterAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var changedIds = new List<string>();
        foreach (var (id, registrations) in Factories.ToArray())
        {
            var previousActive = registrations.Count == 0 ? null : registrations[^1];
            registrations.RemoveAll(registration => registration.Owner == assembly);
            if (registrations.Count == 0) Factories.Remove(id);
            if (previousActive is null || ReferenceEquals(previousActive,
                    registrations.Count == 0 ? null : registrations[^1])) continue;
            changedIds.Add(id);
        }
        if (changedIds.Count == 0) return;
        List<Exception>? failures = null;
        foreach (var id in changedIds) failures = RemoveCachedContributor(id, failures);
        InvalidateFactoryOrder();
        ThrowDisposalFailures(failures);
    }

    public static bool HasRegistrationsFrom(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        foreach (var registrations in Factories.Values)
            foreach (var registration in registrations) if (registration.Owner == assembly) return true;
        foreach (var cache in Instances.Values)
        {
            foreach (var contributor in cache.ById.Values) if (contributor.GetType().Assembly == assembly) return true;
            foreach (var contributor in cache.Ordered) if (contributor.GetType().Assembly == assembly) return true;
        }
        foreach (var factory in _orderedFactories) if (factory.Value.Method.DeclaringType?.Assembly == assembly) return true;
        return false;
    }

    public static void Release(IGraphicsDevice device)
    {
        if (!Instances.Remove(device, out var contributors)) return;
        List<Exception>? failures = null;
        foreach (var contributor in contributors.ById.Values)
        {
            try { contributor.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        ThrowDisposalFailures(failures);
    }

    /// <summary>Cached device snapshot. Borrow only until registration changes or the device is released.</summary>
    public static ReadOnlySpan<IRenderContributor> GetContributors(IGraphicsDevice device) => GetContributorArray(device);

    private static IRenderContributor[] GetContributorArray(IGraphicsDevice device)
    {
        if (!Instances.TryGetValue(device, out var cache))
            Instances[device] = cache = new DeviceContributors();
        for (var attempt = 0; attempt < 16; attempt++)
        {
            if (cache.Version == _factoryVersion) return cache.Ordered;
            if (_orderedFactoryVersion != _factoryVersion)
            {
                _orderedFactories = Factories.Where(static pair => pair.Value.Count > 0)
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => new KeyValuePair<string, Func<IGraphicsDevice, IRenderContributor>>(
                        pair.Key, pair.Value[^1].Factory)).ToArray();
                _orderedFactoryVersion = _factoryVersion;
            }
            // Loading a renderer's dependency can execute another package's module initializer.
            // Never enumerate the mutable global array or publish a partially filled snapshot.
            var factories = _orderedFactories; var version = _factoryVersion;
            var ordered = new IRenderContributor[factories.Length];
            for (var index = 0; index < factories.Length; index++)
            {
                var (id, factory) = factories[index];
                if (!cache.ById.TryGetValue(id, out var contributor))
                {
                    contributor = factory(device) ?? throw new InvalidOperationException($"Render factory '{id}' returned null.");
                    if (!Factories.TryGetValue(id, out var active) || active.Count == 0 || active[^1].Factory != factory)
                    { contributor.Dispose(); break; }
                    cache.ById[id] = contributor;
                }
                ordered[index] = contributor;
            }
            if (version != _factoryVersion) continue;
            cache.Ordered = ordered; cache.Version = version; return ordered;
        }
        throw new InvalidOperationException("Render package registration did not stabilize during initialization.");
    }

    private static void ActiveFactoryChanged(string id)
    {
        List<Exception>? failures = null;
        failures = RemoveCachedContributor(id, failures);
        InvalidateFactoryOrder();
        ThrowDisposalFailures(failures);
    }

    private static List<Exception>? RemoveCachedContributor(string id, List<Exception>? failures)
    {
        foreach (var cache in Instances.Values)
        {
            if (cache.ById.Remove(id, out var contributor))
            {
                try { contributor.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            cache.Ordered = [];
            cache.Version = -1;
        }
        return failures;
    }

    private static void ThrowDisposalFailures(List<Exception>? failures)
    {
        if (failures is { Count: > 0 })
            throw new AggregateException("Scene render contributor disposal failed.", failures);
    }

    private static void InvalidateFactoryOrder()
    {
        _orderedFactories = [];
        _orderedFactoryVersion = -1;
        _factoryVersion = unchecked(_factoryVersion + 1);
    }

    private sealed record Registration(
        Func<IGraphicsDevice, IRenderContributor> Factory,
        Assembly Owner);

    private sealed class DeviceContributors
    {
        internal readonly Dictionary<string, IRenderContributor> ById = new(StringComparer.Ordinal);
        internal IRenderContributor[] Ordered = [];
        internal int Version = -1;
    }
}
