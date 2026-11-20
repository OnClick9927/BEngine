using System.Collections;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.Editor;

/// <summary>
/// Resolves late editor contributions against an existing host. Constructor
/// activation uses the standard DI activator; only extension-owned instances are
/// cached and disposed here. Host services keep their original scope and owner.
/// </summary>
internal sealed class EditorContributionServices : IServiceProvider, IKeyedServiceProvider,
    IServiceProviderIsService, IServiceProviderIsKeyedService, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    private readonly EditorContributionServices _root;
    private readonly IServiceProvider? _host;
    private readonly IServiceScope? _hostScope;
    private readonly ServiceDescriptor[] _registrations;
    private readonly Dictionary<ServiceKey, List<int>> _index;
    private readonly Dictionary<InstanceKey, object?> _instances = [];
    private readonly List<object> _owned = [];
    private readonly HashSet<object> _ownedSet = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _borrowed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<InstanceKey> _resolving = [];
    private readonly object _gate;
    private bool _disposed;

    internal EditorContributionServices(IServiceProvider? host, ServiceDescriptor[] registrations)
    {
        _root = this;
        _host = host;
        _gate = new();
        _registrations = registrations;
        _index = [];
        for (var i = 0; i < registrations.Length; i++)
        {
            var key = new ServiceKey(registrations[i].ServiceType, registrations[i].ServiceKey);
            if (!_index.TryGetValue(key, out var entries)) _index.Add(key, entries = []);
            entries.Add(i);
        }
    }

    private EditorContributionServices(EditorContributionServices root, IServiceProvider? host, IServiceScope? hostScope)
    {
        _root = root;
        _host = host;
        _hostScope = hostScope;
        _gate = root._gate;
        _registrations = root._registrations;
        _index = root._index;
    }

    public object? GetService(Type serviceType) => GetKeyedService(serviceType, null);

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (serviceKey is null)
            {
                if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IKeyedServiceProvider) ||
                    serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService) ||
                    serviceType == typeof(IServiceScopeFactory)) return this;
            }
            if (serviceType.IsConstructedGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return GetEnumerable(serviceType, serviceKey);
            if (Equals(serviceKey, KeyedService.AnyKey))
                throw new InvalidOperationException("AnyKey can only be used to enumerate keyed services.");
            if (Borrow(serviceType, serviceKey) is { } inherited) return inherited;
            if (TryGetEntries(serviceType, serviceKey, out var entries))
                return Resolve(entries[^1], serviceType, serviceKey);
            return null;
        }
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        GetKeyedService(serviceType, serviceKey) ?? throw new InvalidOperationException(
            $"No service for type '{serviceType}' and key '{serviceKey}' has been registered.");

    public bool IsService(Type serviceType) => IsKeyedService(serviceType, null);

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (serviceType.IsGenericTypeDefinition) return false;
            if (serviceKey is null && (serviceType == typeof(IServiceProvider) || serviceType == typeof(IKeyedServiceProvider) ||
                serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService) ||
                serviceType == typeof(IServiceScopeFactory))) return true;
            if (serviceType.IsConstructedGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)) return true;
            var available = serviceKey is null
                ? _host?.GetService<IServiceProviderIsService>()?.IsService(serviceType)
                : _host?.GetService<IServiceProviderIsKeyedService>()?.IsKeyedService(serviceType, serviceKey);
            return available == true || TryGetEntries(serviceType, serviceKey, out _) ||
                (available is null && Borrow(serviceType, serviceKey) is not null);
        }
    }

    private object? Borrow(Type serviceType, object? key)
    {
        var instance = key is null ? _host?.GetService(serviceType)
            : (_host as IKeyedServiceProvider)?.GetKeyedService(serviceType, key);
        if (instance is IDisposable or IAsyncDisposable)
            _borrowed.Add(instance);
        return instance;
    }

    private bool TryGetEntries(Type type, object? key, out List<int> entries)
    {
        if (_index.TryGetValue(new(type, key), out entries!)) return true;
        if (type.IsConstructedGenericType && _index.TryGetValue(new(type.GetGenericTypeDefinition(), key), out entries!)) return true;
        if (key is not null && !Equals(key, KeyedService.AnyKey))
        {
            if (_index.TryGetValue(new(type, KeyedService.AnyKey), out entries!)) return true;
            if (type.IsConstructedGenericType &&
                _index.TryGetValue(new(type.GetGenericTypeDefinition(), KeyedService.AnyKey), out entries!)) return true;
        }
        entries = null!;
        return false;
    }

    private Array GetEnumerable(Type enumerableType, object? key)
    {
        var type = enumerableType.GenericTypeArguments[0];
        var values = new List<object?>();
        if (Borrow(enumerableType, key) is IEnumerable inherited)
            foreach (var instance in inherited)
            {
                values.Add(instance);
                if (instance is IDisposable or IAsyncDisposable)
                    _borrowed.Add(instance);
            }
        for (var i = 0; i < _registrations.Length; i++)
        {
            var registration = _registrations[i];
            var keyMatches = Equals(key, KeyedService.AnyKey)
                ? registration.ServiceKey is not null && !Equals(registration.ServiceKey, KeyedService.AnyKey)
                : Equals(registration.ServiceKey, key);
            if (!keyMatches ||
                (registration.ServiceType != type && (!type.IsConstructedGenericType ||
                 registration.ServiceType != type.GetGenericTypeDefinition()))) continue;
            if (registration.ServiceType.IsGenericTypeDefinition)
            {
                var implementation = registration.IsKeyedService ? registration.KeyedImplementationType : registration.ImplementationType;
                try { _ = implementation!.MakeGenericType(type.GenericTypeArguments); }
                catch (ArgumentException) { continue; }
            }
            values.Add(Resolve(i, type, registration.ServiceKey));
        }
        var result = Array.CreateInstance(type, values.Count);
        for (var i = 0; i < values.Count; i++) result.SetValue(values[i], i);
        return result;
    }

    private object? Resolve(int index, Type serviceType, object? key)
    {
        var registration = _registrations[index];
        var instance = registration.IsKeyedService ? registration.KeyedImplementationInstance : registration.ImplementationInstance;
        if (instance is not null) return instance; // External instance registrations retain their owner.
        var owner = registration.Lifetime == ServiceLifetime.Singleton ? _root : this;
        owner.ThrowIfDisposed();
        var cacheKey = new InstanceKey(index, serviceType, key);
        if (registration.Lifetime != ServiceLifetime.Transient && owner._instances.TryGetValue(cacheKey, out var cached)) return cached;
        if (!owner._resolving.Add(cacheKey)) throw new InvalidOperationException($"Circular editor service dependency: '{serviceType}'.");
        try
        {
            if (registration.IsKeyedService && registration.KeyedImplementationFactory is { } keyedFactory)
                instance = keyedFactory(owner, key);
            else if (!registration.IsKeyedService && registration.ImplementationFactory is { } factory)
                instance = factory(owner);
            else
            {
                var implementation = registration.IsKeyedService ? registration.KeyedImplementationType! : registration.ImplementationType!;
                if (implementation.IsGenericTypeDefinition) implementation = implementation.MakeGenericType(serviceType.GenericTypeArguments);
                instance = ActivatorUtilities.CreateInstance(owner, implementation);
            }
            if (instance is IDisposable or IAsyncDisposable && !owner._borrowed.Contains(instance) && owner._ownedSet.Add(instance))
                owner._owned.Add(instance);
            if (registration.Lifetime != ServiceLifetime.Transient) owner._instances.Add(cacheKey, instance);
            return instance;
        }
        finally { owner._resolving.Remove(cacheKey); }
    }

    public IServiceScope CreateScope()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            var scope = _host?.GetService<IServiceScopeFactory>()?.CreateScope();
            return new ContributionScope(new EditorContributionServices(_root, scope?.ServiceProvider ?? _host, scope));
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed || _root._disposed, this);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            List<Exception>? errors = null;
            for (var i = _owned.Count - 1; i >= 0; i--)
                try
                {
                    if (_owned[i] is IDisposable disposable) disposable.Dispose();
                    else throw new InvalidOperationException($"'{_owned[i].GetType()}' requires asynchronous disposal.");
                }
                catch (Exception exception) { (errors ??= []).Add(exception); }
            try { _hostScope?.Dispose(); }
            catch (Exception exception) { (errors ??= []).Add(exception); }
            Clear();
            if (errors is not null) throw new AggregateException(errors);
        }
    }

    public async ValueTask DisposeAsync()
    {
        object[] owned;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            owned = _owned.ToArray();
            Clear();
        }
        List<Exception>? errors = null;
        for (var i = owned.Length - 1; i >= 0; i--)
            try
            {
                if (owned[i] is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else ((IDisposable)owned[i]).Dispose();
            }
            catch (Exception exception) { (errors ??= []).Add(exception); }
        try
        {
            if (_hostScope is IAsyncDisposable scope) await scope.DisposeAsync().ConfigureAwait(false);
            else _hostScope?.Dispose();
        }
        catch (Exception exception) { (errors ??= []).Add(exception); }
        if (errors is not null) throw new AggregateException(errors);
    }

    private void Clear()
    {
        _owned.Clear();
        _ownedSet.Clear();
        _borrowed.Clear();
        _instances.Clear();
        _resolving.Clear();
    }

    private readonly record struct ServiceKey(Type Type, object? Key);
    private readonly record struct InstanceKey(int Registration, Type Type, object? Key);
    private sealed class ContributionScope(EditorContributionServices services) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider => services;
        public void Dispose() => services.Dispose();
        public ValueTask DisposeAsync() => services.DisposeAsync();
    }
}
