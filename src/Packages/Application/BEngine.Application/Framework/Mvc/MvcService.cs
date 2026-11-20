using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.App.Mvc;

/// <summary>
/// Explicit model/controller/view composition driven from the game thread.
/// Instances belong to this service unless registered as borrowed. Constructor dependencies
/// can use earlier roles or earlier registrations; all instances in a role exist before that role initializes.
/// </summary>
public sealed class MvcService : IGameService
{
    private enum Phase { Configuring, Initializing, Ready, Faulted, Disposing, Disposed }
    private readonly List<Registration> _registrations = [];
    private readonly Dictionary<Type, Registration> _byType = [];
    private readonly HashSet<IMvcElement> _instances = new(ReferenceEqualityComparer.Instance);
    private readonly LocalServices _services;
    private Phase _phase;
    private bool _ticking;
    private IMvcUpdate[] _updates = [];
    private IMvcFixedUpdate[] _fixedUpdates = [];
    private IMvcLateUpdate[] _lateUpdates = [];

    public MvcService(IServiceProvider services) => _services = new LocalServices(this, services ?? throw new ArgumentNullException(nameof(services)));
    public bool IsInitialized => _phase == Phase.Ready;

    void IGameService.Initialize(GameDomain domain) => Initialize(domain.Lifetime.Token);
    void IGameService.Update(Fix64 deltaTime) => Tick(deltaTime);
    void IGameService.FixedUpdate(Fix64 deltaTime) => FixedTick(deltaTime);
    void IGameService.LateUpdate(Fix64 deltaTime) => LateTick(deltaTime);

    public MvcService RegisterModel<T>(T model, bool ownsInstance = true) where T : class, IMvcModel => Register(model, 0, ownsInstance);
    public MvcService RegisterCtrl<T>(T controller, bool ownsInstance = true) where T : class, IMvcCtrl => Register(controller, 1, ownsInstance);
    public MvcService RegisterView<T>(T view, bool ownsInstance = true) where T : class, IMvcView => Register(view, 2, ownsInstance);
    public MvcService RegisterModel<T>(Func<IServiceProvider, T> factory, bool ownsInstance = true) where T : class, IMvcModel => Register(factory, 0, ownsInstance);
    public MvcService RegisterCtrl<T>(Func<IServiceProvider, T> factory, bool ownsInstance = true) where T : class, IMvcCtrl => Register(factory, 1, ownsInstance);
    public MvcService RegisterView<T>(Func<IServiceProvider, T> factory, bool ownsInstance = true) where T : class, IMvcView => Register(factory, 2, ownsInstance);
    public MvcService RegisterModel<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IMvcModel =>
        RegisterModel<T>(static services => ActivatorUtilities.CreateInstance<T>(services));
    public MvcService RegisterCtrl<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IMvcCtrl =>
        RegisterCtrl<T>(static services => ActivatorUtilities.CreateInstance<T>(services));
    public MvcService RegisterView<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>() where T : class, IMvcView =>
        RegisterView<T>(static services => ActivatorUtilities.CreateInstance<T>(services));

    private MvcService Register<T>(T instance, int role, bool ownsInstance) where T : class, IMvcElement
    {
        ArgumentNullException.ThrowIfNull(instance); EnsureConfiguring();
        if (_instances.Contains(instance)) throw new InvalidOperationException("The same MVC element cannot be registered twice.");
        Add(new Registration(typeof(T), role, null, instance, ownsInstance));
        _instances.Add(instance); return this;
    }
    private MvcService Register<T>(Func<IServiceProvider, T> factory, int role, bool ownsInstance) where T : class, IMvcElement
    {
        ArgumentNullException.ThrowIfNull(factory); EnsureConfiguring();
        Add(new Registration(typeof(T), role, provider => factory(provider), null, ownsInstance)); return this;
    }
    private void Add(Registration registration)
    {
        if (!_byType.TryAdd(registration.Type, registration)) throw new InvalidOperationException($"MVC type '{registration.Type}' is already registered.");
        registration.Order = _registrations.Count;
        _registrations.Add(registration);
    }

    public T Get<T>() where T : class, IMvcElement => (T)Get(typeof(T));
    public T GetModel<T>() where T : class, IMvcModel => Get<T>();
    public T GetCtrl<T>() where T : class, IMvcCtrl => Get<T>();
    public T GetView<T>() where T : class, IMvcView => Get<T>();
    public IMvcElement Get(Type type)
    {
        ArgumentNullException.ThrowIfNull(type); EnsureUsable();
        if (!_byType.TryGetValue(type, out var registration)) throw new KeyNotFoundException($"MVC type '{type}' is not registered.");
        return registration.Instance ?? throw new InvalidOperationException($"MVC type '{type}' has not been constructed. Constructors may depend on earlier roles or earlier registrations.");
    }
    public bool TryGet<T>([NotNullWhen(true)] out T? instance) where T : class, IMvcElement
    {
        EnsureUsable();
        if (_byType.TryGetValue(typeof(T), out var registration) && registration.Instance is T found)
        { instance = found; return true; }
        instance = null; return false;
    }

    public void Initialize(BCancellationToken cancellationToken = default)
    {
        EnsureConfiguring(); cancellationToken.ThrowIfCancellationRequested(); _phase = Phase.Initializing;
        try
        {
            // Stable within each role even when registrations from different roles were interleaved.
            _registrations.Sort(static (left, right) => left.Role != right.Role ? left.Role.CompareTo(right.Role) : left.Order.CompareTo(right.Order));
            for (var role = 0; role != 3; role++)
            {
                foreach (var registration in _registrations)
                {
                    if (registration.Role != role) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    if (registration.Instance is not null) continue;
                    var instance = registration.Factory!(_services) ?? throw new InvalidOperationException($"MVC factory '{registration.Type}' returned null.");
                    if (!_instances.Add(instance)) throw new InvalidOperationException("MVC factories must return distinct instances.");
                    registration.Instance = instance; registration.Factory = null;
                }
                foreach (var registration in _registrations)
                    if (registration.Role == role)
                    { cancellationToken.ThrowIfCancellationRequested(); registration.Instance!.Initialize(_services); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var updates = new List<IMvcUpdate>(); var fixedUpdates = new List<IMvcFixedUpdate>(); var lateUpdates = new List<IMvcLateUpdate>();
            foreach (var registration in _registrations)
            {
                if (registration.Instance is IMvcUpdate update) updates.Add(update);
                if (registration.Instance is IMvcFixedUpdate fixedUpdate) fixedUpdates.Add(fixedUpdate);
                if (registration.Instance is IMvcLateUpdate lateUpdate) lateUpdates.Add(lateUpdate);
            }
            _updates = updates.ToArray(); _fixedUpdates = fixedUpdates.ToArray(); _lateUpdates = lateUpdates.ToArray();
            _phase = Phase.Ready;
        }
        catch (Exception error)
        {
            List<Exception>? errors = [error];
            Cleanup(ref errors); _phase = Phase.Faulted;
            MvcErrors.Throw(errors); throw;
        }
    }

    public void Tick(Fix64 deltaTime)
    {
        BeginTick();
        try { for (var index = 0; index < _updates.Length; index++) _updates[index].Update(deltaTime); }
        finally { _ticking = false; }
    }
    public void FixedTick(Fix64 deltaTime)
    {
        BeginTick();
        try { for (var index = 0; index < _fixedUpdates.Length; index++) _fixedUpdates[index].FixedUpdate(deltaTime); }
        finally { _ticking = false; }
    }
    public void LateTick(Fix64 deltaTime)
    {
        BeginTick();
        try { for (var index = 0; index < _lateUpdates.Length; index++) _lateUpdates[index].LateUpdate(deltaTime); }
        finally { _ticking = false; }
    }
    private void BeginTick()
    {
        EnsureUsable();
        if (_phase != Phase.Ready) throw new InvalidOperationException("Initialize MVC before ticking it.");
        if (_ticking) throw new InvalidOperationException("MVC ticks cannot be reentered.");
        _ticking = true;
    }

    public void Dispose()
    {
        if (_phase == Phase.Disposed || _phase == Phase.Disposing) return;
        if (_phase == Phase.Initializing || _ticking) throw new InvalidOperationException("MVC cannot be disposed from its lifecycle callback.");
        _phase = Phase.Disposing;
        List<Exception>? errors = null;
        try { Cleanup(ref errors); }
        finally { _phase = Phase.Disposed; }
        MvcErrors.Throw(errors);
    }
    private void Cleanup(ref List<Exception>? errors)
    {
        // Uninitialized explicit instances are owned too. Sorting also makes pre-init Dispose deterministic.
        _registrations.Sort(static (left, right) => left.Role != right.Role ? left.Role.CompareTo(right.Role) : left.Order.CompareTo(right.Order));
        for (var index = _registrations.Count - 1; index >= 0; index--)
        {
            var registration = _registrations[index];
            if (registration.OwnsInstance && registration.Instance is { } instance)
                try { instance.Dispose(); } catch (Exception error) { MvcErrors.Add(ref errors, error); }
            registration.Instance = null; registration.Factory = null;
        }
        _registrations.Clear(); _byType.Clear(); _instances.Clear();
        _updates = []; _fixedUpdates = []; _lateUpdates = [];
        _services.Detach();
    }
    private void EnsureConfiguring()
    {
        EnsureUsable();
        if (_phase != Phase.Configuring) throw new InvalidOperationException("Register and initialize MVC only once, before it starts.");
    }
    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_phase == Phase.Disposed, this);
        if (_phase == Phase.Faulted) throw new InvalidOperationException("MVC initialization failed; create a new service to retry.");
    }

    // This delegates service creation to the caller's DI container. It only exposes
    // already-created MVC instances to standard ActivatorUtilities constructor injection.
    private sealed class LocalServices(MvcService owner, IServiceProvider parent) : IServiceProvider,
        IServiceProviderIsService, IKeyedServiceProvider, IServiceProviderIsKeyedService
    {
        private IServiceProvider? _parent = parent;
        public object? GetService(Type serviceType)
        {
            if (_parent is null) throw new ObjectDisposedException(nameof(MvcService));
            if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceProviderIsService) ||
                serviceType == typeof(IServiceProviderIsKeyedService)) return this;
            if (serviceType == typeof(MvcService)) return owner;
            if (owner._byType.TryGetValue(serviceType, out var registration) && registration.Instance is { } instance) return instance;
            return _parent.GetService(serviceType);
        }
        public bool IsService(Type serviceType) => serviceType == typeof(IServiceProvider) ||
            serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService) ||
            serviceType == typeof(MvcService) ||
            owner._byType.ContainsKey(serviceType) ||
            (_parent?.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService)?.IsService(serviceType) == true;
        public object? GetKeyedService(Type serviceType, object? serviceKey) => serviceKey is null
            ? GetService(serviceType)
            : (_parent ?? throw new ObjectDisposedException(nameof(MvcService))) is IKeyedServiceProvider keyed
                ? keyed.GetKeyedService(serviceType, serviceKey) : null;
        public object GetRequiredKeyedService(Type serviceType, object? serviceKey) => GetKeyedService(serviceType, serviceKey)
            ?? throw new InvalidOperationException($"Required keyed service '{serviceType}' ({serviceKey}) was not registered.");
        public bool IsKeyedService(Type serviceType, object? serviceKey) => serviceKey is null ? IsService(serviceType) :
            (_parent?.GetService(typeof(IServiceProviderIsKeyedService)) as IServiceProviderIsKeyedService)?.IsKeyedService(serviceType, serviceKey) == true;
        internal void Detach() => _parent = null;
    }
    private sealed class Registration(Type type, int role, Func<IServiceProvider, IMvcElement>? factory, IMvcElement? instance, bool ownsInstance)
    {
        internal readonly Type Type = type;
        internal readonly int Role = role;
        internal int Order;
        internal Func<IServiceProvider, IMvcElement>? Factory = factory;
        internal IMvcElement? Instance = instance;
        internal readonly bool OwnsInstance = ownsInstance;
    }
}
