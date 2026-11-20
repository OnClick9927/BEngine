using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.App;

/// <summary>Explicit domain composition. The same type can have many distinct instances.
/// Registrations freeze before initialization; resolve an ambiguous type by key or GetAll.</summary>
public sealed class GameServiceCollection : IServiceProvider, IKeyedServiceProvider
{
    private readonly GameDomain _domain;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<Type, object> _snapshots = [];
    private IServiceProvider? _fallback;
    private bool _frozen;
    private bool _disposed;
    internal GameServiceCollection(GameDomain domain) => _domain = domain;
    public int Count => _entries.Count;
    public IGameService this[int index] => _entries[index].Service;

    public T Add<T>(T service, string? key = null, bool ownsInstance = true) where T : class, IGameService
    {
        ArgumentNullException.ThrowIfNull(service);
        EnsureUsable();
        if (_frozen) throw new InvalidOperationException("Register services before domain initialization.");
        key = Normalize(key);
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.Service, service)) throw new InvalidOperationException("The same service instance cannot be registered twice.");
            if (key is not null && entry.Service.GetType() == service.GetType() && entry.Key == key)
                throw new InvalidOperationException($"Service '{service.GetType()}' already has key '{key}'.");
        }
        _entries.Add(new Entry(service, key, ownsInstance));
        _snapshots.Clear();
        return service;
    }

    public T Get<T>(string? key = null) where T : class => (T)(Find(typeof(T), Normalize(key)) ??
        throw new KeyNotFoundException($"No domain service '{typeof(T)}' with key '{key}' is registered."));
    public bool TryGet<T>([NotNullWhen(true)] out T? service, string? key = null) where T : class
    {
        service = (T?)Find(typeof(T), Normalize(key));
        return service is not null;
    }
    public IReadOnlyList<T> GetAll<T>() where T : class
    {
        EnsureUsable();
        if (_snapshots.TryGetValue(typeof(T), out var cached)) return (IReadOnlyList<T>)cached;
        var items = new List<T>();
        foreach (var entry in _entries) if (entry.Service is T typed) items.Add(typed);
        var snapshot = items.AsReadOnly();
        _snapshots.Add(typeof(T), snapshot);
        return snapshot;
    }
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        var found = Find(serviceType, null);
        if (found is not null) return found;
        if (serviceType == typeof(GameDomain)) return _domain;
        if (serviceType == typeof(GameServiceCollection) || serviceType == typeof(IServiceProvider) || serviceType == typeof(IKeyedServiceProvider)) return this;
        return _fallback?.GetService(serviceType);
    }
    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is null) return GetService(serviceType);
        var found = serviceKey is string key ? Find(serviceType, Normalize(key)) : null;
        if (found is not null) return found;
        return _fallback is IKeyedServiceProvider keyed ? keyed.GetKeyedService(serviceType, serviceKey) : null;
    }
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) => GetKeyedService(serviceType, serviceKey) ??
        throw new InvalidOperationException($"No service '{serviceType}' with key '{serviceKey}' is registered.");

    private object? Find(Type type, string? key)
    {
        EnsureUsable();
        object? result = null;
        foreach (var entry in _entries)
        {
            if ((key is not null && entry.Key != key) || !type.IsInstanceOfType(entry.Service)) continue;
            if (result is not null) throw new InvalidOperationException($"More than one domain service matches '{type}'. Use a unique key or GetAll<T>().");
            result = entry.Service;
        }
        return result;
    }
    internal void SetFallback(IServiceProvider provider) => _fallback = provider;
    internal void Initialize()
    {
        _frozen = true;
        for (var i = 0; i < _entries.Count; i++)
        {
            _domain.Lifetime.Token.ThrowIfCancellationRequested();
            _entries[i].Service.Initialize(_domain);
            _entries[i].Initialized = true;
        }
    }
    internal void Dispatch(Fix64 deltaTime, int phase)
    {
        for (var i = 0; i < _entries.Count && _domain.IsRunning; i++)
        {
            var service = _entries[i].Service;
            if (phase == 0) service.Update(deltaTime);
            else if (phase == 1) service.FixedUpdate(deltaTime);
            else service.LateUpdate(deltaTime);
        }
    }
    internal void Notify(int kind, bool value)
    {
        for (var i = 0; i < _entries.Count && _domain.IsRunning; i++)
        {
            var service = _entries[i].Service;
            if (kind == 0) service.Focus(value);
            else if (kind == 1) service.Pause(value);
            else service.Disable();
        }
    }
    internal void Cleanup(bool completedStartup, ref List<Exception>? failures)
    {
        if (_disposed) return;
        _frozen = true;
        if (completedStartup)
            for (var i = _entries.Count - 1; i >= 0; i--)
                if (_entries[i].Initialized)
                    try { _entries[i].Service.OnDomainStopping(); } catch (Exception error) { (failures ??= []).Add(error); }
        for (var i = _entries.Count - 1; i >= 0; i--)
            if (_entries[i].OwnsInstance)
                try { _entries[i].Service.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        _disposed = true;
        _entries.Clear(); _snapshots.Clear(); _fallback = null;
    }
    private void EnsureUsable() => ObjectDisposedException.ThrowIf(_disposed, this);
    private static string? Normalize(string? key) => string.IsNullOrEmpty(key) ? null : key;
    private sealed class Entry(IGameService service, string? key, bool ownsInstance)
    {
        internal readonly IGameService Service = service;
        internal readonly string? Key = key;
        internal readonly bool OwnsInstance = ownsInstance;
        internal bool Initialized;
    }
}
