using BEngine.Build;
using System.Reflection;

namespace BEngine.HotUpdate;

public sealed class HotUpdateActivationGate
{
    private readonly IManagedCodeRuntimeFactory _runtimeFactory;
    private readonly HashSet<string> _hostCapabilities;

    public HotUpdateActivationGate(
        IManagedCodeRuntimeFactory runtimeFactory,
        IEnumerable<string>? hostCapabilities = null)
    {
        _runtimeFactory = runtimeFactory ?? throw new ArgumentNullException(nameof(runtimeFactory));
        _hostCapabilities = new HashSet<string>(
            hostCapabilities ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public async BValueTask<PreparedHotUpdateDomain> PrepareAsync(
        ManagedCodeRelease release,
        ManagedCodeRuntimeRequest runtimeRequest,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.ContractVersion != HotUpdateContract.CurrentVersion)
            throw new InvalidDataException(
                $"HotUpdate release contract {release.ContractVersion} is incompatible with host contract " +
                $"{HotUpdateContract.CurrentVersion}.");
        var runtime = _runtimeFactory.CreateRuntime(runtimeRequest);
        Assembly[] loadedAssemblies = [];
        Assembly[] ownedAssemblies = [];
        try
        {
            var loaded = await runtime.LoadAsync(release, cancellationToken);
            loadedAssemblies = loaded.Assemblies.ToArray();
            ownedAssemblies = loaded.OwnedAssemblies.ToArray();
            var ordered = ValidateAndOrderModules(loaded.Modules);
            // AssemblyLoad may have caused an eager, incomplete scan before every release dependency was present.
            // Re-register the completed release as one unit and make this domain its cache owner.
            RuntimeTypeCache.UnregisterAssemblies(ownedAssemblies);
            RuntimeTypeCache.RegisterAssemblies(ownedAssemblies);
            return new PreparedHotUpdateDomain(
                release, runtime, ordered, loadedAssemblies, ownedAssemblies, _hostCapabilities);
        }
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            try { HotUpdateAssemblyCleanup.Unregister(ownedAssemblies); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            try { await runtime.DisposeAsync(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (cleanupFailures is { Count: > 0 })
                throw new AggregateException(
                    "HotUpdate preparation and assembly cleanup both failed.",
                    [failure, .. cleanupFailures]);
            throw;
        }
    }

    private IHotUpdateModule[] ValidateAndOrderModules(IReadOnlyList<IHotUpdateModule> modules)
    {
        var descriptors = new Dictionary<string, IHotUpdateModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in modules)
        {
            var descriptor = module.Descriptor ??
                             throw new InvalidDataException(
                                 $"HotUpdate module '{module.GetType().FullName}' has no descriptor.");
            if (string.IsNullOrWhiteSpace(descriptor.ModuleId))
                throw new InvalidDataException(
                    $"HotUpdate module '{module.GetType().FullName}' has no module id.");
            if (descriptor.ContractVersion != HotUpdateContract.CurrentVersion)
                throw new InvalidDataException(
                    $"HotUpdate module '{descriptor.ModuleId}' contract {descriptor.ContractVersion} is " +
                    $"incompatible with host contract {HotUpdateContract.CurrentVersion}.");
            var missing = descriptor.RequiredCapabilities
                .Where(capability => !_hostCapabilities.Contains(capability)).ToArray();
            if (missing.Length != 0)
                throw new PlatformNotSupportedException(
                    $"HotUpdate module '{descriptor.ModuleId}' requires unavailable host capabilities: " +
                    string.Join(", ", missing));
            if (!descriptors.TryAdd(descriptor.ModuleId, module))
                throw new InvalidDataException($"Duplicate HotUpdate module id '{descriptor.ModuleId}'.");
        }
        return ManagedCodeGraph.Order(
            descriptors.Values,
            static module => module.Descriptor.ModuleId,
            static module => module.Descriptor.Dependencies);
    }
}

public sealed class PreparedHotUpdateDomain
{
    private readonly IManagedCodeRuntime _runtime;
    private IHotUpdateModule[] _modules;
    private Assembly[] _assemblies;
    private Assembly[] _ownedAssemblies;
    private readonly HashSet<string> _hostCapabilities;
    private int _state;
    private IDisposable? _typePreference;

    internal PreparedHotUpdateDomain(
        ManagedCodeRelease release,
        IManagedCodeRuntime runtime,
        IHotUpdateModule[] modules,
        IEnumerable<Assembly> assemblies,
        IEnumerable<Assembly> ownedAssemblies,
        IEnumerable<string> hostCapabilities)
    {
        Release = release;
        _runtime = runtime;
        _modules = modules;
        _assemblies = assemblies.ToArray();
        _ownedAssemblies = ownedAssemblies.ToArray();
        _hostCapabilities = new HashSet<string>(hostCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    public ManagedCodeRelease Release { get; }
    public ManagedCodeRuntimeKind RuntimeKind => _runtime.Kind;
    public IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);
    public IReadOnlyList<Assembly> OwnedAssemblies => Array.AsReadOnly(_ownedAssemblies);
    public IReadOnlyList<string> ModuleIds => Array.AsReadOnly(
        _modules.Select(module => module.Descriptor.ModuleId).ToArray());

    public async BValueTask<HotUpdateDomain> ActivateAsync(
        IServiceProvider hostServices,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        if (_state != 0)
            throw new InvalidOperationException("The prepared HotUpdate domain was already activated or disposed.");
        _state = 1;
        var services = new HotUpdateServiceRegistry();
        var modules = _modules;
        var context = new ModuleContext(
            Release.ReleaseId, hostServices, services, _hostCapabilities);
        var configured = 0;
        var started = 0;
        try
        {
            // A release can replace package types already loaded by AOT. Own the
            // lookup preference in the domain itself, including callers which do
            // not go through PlayerHotUpdateSession (offline AB loading, tools).
            _typePreference = RuntimeTypeCache.PreferAssemblies(_ownedAssemblies);
            while (configured < modules.Length)
            {
                var index = configured++;
                await modules[index].ConfigureAsync(context, cancellationToken);
            }
            while (started < modules.Length)
            {
                var index = started++;
                await modules[index].StartAsync(cancellationToken);
            }
            _state = 2;
            _modules = [];
            _assemblies = [];
            var ownedAssemblies = _ownedAssemblies;
            _ownedAssemblies = [];
            var preference = _typePreference;
            _typePreference = null;
            return new HotUpdateDomain(Release, _runtime, modules, ownedAssemblies, services, preference);
        }
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            // Configure may allocate resources before Start. Stop every module whose Configure was attempted,
            // including the module that threw, and do so before removing its host registrations.
            for (var index = configured - 1; index >= 0; index--)
            {
                try { await modules[index].StopAsync(BCancellationToken.None); }
                catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            }
            try { HotUpdateAssemblyCleanup.Unregister(_ownedAssemblies); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            try { _typePreference?.Dispose(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            _typePreference = null;
            _assemblies = [];
            _ownedAssemblies = [];
            _modules = [];
            try { await _runtime.DisposeAsync(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            _state = 3;
            if (cleanupFailures is { Count: > 0 })
                throw new AggregateException(
                    "HotUpdate activation and rollback both failed.",
                    [failure, .. cleanupFailures]);
            throw;
        }
    }

    public async BValueTask DisposeAsync()
    {
        if (_state == 0)
        {
            _state = 3;
            List<Exception>? failures = null;
            try { HotUpdateAssemblyCleanup.Unregister(_ownedAssemblies); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            _assemblies = [];
            _ownedAssemblies = [];
            _modules = [];
            try { await _runtime.DisposeAsync(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            if (failures is { Count: > 0 })
                throw new AggregateException("Prepared HotUpdate domain shutdown failed.", failures);
        }
    }

    private sealed record ModuleContext(
        string ReleaseId,
        IServiceProvider HostServices,
        IHotUpdateServiceRegistry Services,
        IReadOnlySet<string> HostCapabilities) : IHotUpdateModuleContext;
}

public sealed class HotUpdateDomain
{
    private readonly IManagedCodeRuntime _runtime;
    private IHotUpdateModule[] _modules;
    private Assembly[] _assemblies;
    private IDisposable? _typePreference;
    private int _disposed;

    internal HotUpdateDomain(
        ManagedCodeRelease release,
        IManagedCodeRuntime runtime,
        IHotUpdateModule[] modules,
        Assembly[] ownedAssemblies,
        IHotUpdateServiceRegistry services,
        IDisposable? typePreference)
    {
        Release = release;
        _runtime = runtime;
        _modules = modules;
        _assemblies = ownedAssemblies;
        Services = services;
        _typePreference = typePreference;
    }

    public ManagedCodeRelease Release { get; }
    public ManagedCodeRuntimeKind RuntimeKind => _runtime.Kind;
    public IHotUpdateServiceRegistry Services { get; }

    public async BValueTask DisposeAsync()
    {
        if (_disposed != 0) return;
        _disposed = 1;
        List<Exception>? failures = null;
        for (var index = _modules.Length - 1; index >= 0; index--)
        {
            try { await _modules[index].StopAsync(BCancellationToken.None); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        try { HotUpdateAssemblyCleanup.Unregister(_assemblies); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { _typePreference?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        _typePreference = null;
        _assemblies = [];
        _modules = [];
        try { await _runtime.DisposeAsync(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException("HotUpdate domain shutdown failed.", failures);
    }
}
