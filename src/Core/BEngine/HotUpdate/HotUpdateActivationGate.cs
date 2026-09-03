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
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.ContractVersion != HotUpdateContract.CurrentVersion)
            throw new InvalidDataException(
                $"HotUpdate release contract {release.ContractVersion} is incompatible with host contract " +
                $"{HotUpdateContract.CurrentVersion}.");
        var runtime = _runtimeFactory.CreateRuntime(runtimeRequest);
        Assembly[] loadedAssemblies = [];
        try
        {
            var loaded = await runtime.LoadAsync(release, cancellationToken).ConfigureAwait(false);
            loadedAssemblies = loaded.Assemblies.ToArray();
            var ordered = ValidateAndOrderModules(loaded.Modules);
            // AssemblyLoad may have caused an eager, incomplete scan before every release dependency was present.
            // Re-register the completed release as one unit and make this domain its cache owner.
            RuntimeTypeCache.UnregisterAssemblies(loadedAssemblies);
            RuntimeTypeCache.RegisterAssemblies(loadedAssemblies);
            return new PreparedHotUpdateDomain(
                release, runtime, ordered, loadedAssemblies, _hostCapabilities);
        }
        catch
        {
            RuntimeTypeCache.UnregisterAssemblies(loadedAssemblies);
            await runtime.DisposeAsync().ConfigureAwait(false);
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

public sealed class PreparedHotUpdateDomain : IAsyncDisposable
{
    private readonly IManagedCodeRuntime _runtime;
    private IHotUpdateModule[] _modules;
    private Assembly[] _assemblies;
    private readonly HashSet<string> _hostCapabilities;
    private int _state;

    internal PreparedHotUpdateDomain(
        ManagedCodeRelease release,
        IManagedCodeRuntime runtime,
        IHotUpdateModule[] modules,
        IEnumerable<Assembly> assemblies,
        IEnumerable<string> hostCapabilities)
    {
        Release = release;
        _runtime = runtime;
        _modules = modules;
        _assemblies = assemblies.ToArray();
        _hostCapabilities = new HashSet<string>(hostCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    public ManagedCodeRelease Release { get; }
    public ManagedCodeRuntimeKind RuntimeKind => _runtime.Kind;
    public IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);

    public async BValueTask<HotUpdateDomain> ActivateAsync(
        IServiceProvider hostServices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hostServices);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The prepared HotUpdate domain was already activated or disposed.");
        var services = new HotUpdateServiceRegistry();
        var modules = _modules;
        var context = new ModuleContext(
            Release.ReleaseId, hostServices, services, _hostCapabilities);
        var configured = 0;
        var started = 0;
        try
        {
            for (; configured < modules.Length; configured++)
                await modules[configured].ConfigureAsync(context, cancellationToken).ConfigureAwait(false);
            for (; started < modules.Length; started++)
                await modules[started].StartAsync(cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _state, 2);
            _modules = [];
            var assemblies = _assemblies;
            _assemblies = [];
            return new HotUpdateDomain(Release, _runtime, modules, assemblies, services);
        }
        catch
        {
            for (var index = started - 1; index >= 0; index--)
            {
                try { await modules[index].StopAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
            RuntimeTypeCache.UnregisterAssemblies(_assemblies);
            _assemblies = [];
            _modules = [];
            await _runtime.DisposeAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref _state, 3);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 3, 0) == 0)
        {
            RuntimeTypeCache.UnregisterAssemblies(_assemblies);
            _assemblies = [];
            _modules = [];
            await _runtime.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed record ModuleContext(
        string ReleaseId,
        IServiceProvider HostServices,
        IHotUpdateServiceRegistry Services,
        IReadOnlySet<string> HostCapabilities) : IHotUpdateModuleContext;
}

public sealed class HotUpdateDomain : IDisposable, IAsyncDisposable
{
    private readonly IManagedCodeRuntime _runtime;
    private IHotUpdateModule[] _modules;
    private Assembly[] _assemblies;
    private int _disposed;

    internal HotUpdateDomain(
        ManagedCodeRelease release,
        IManagedCodeRuntime runtime,
        IHotUpdateModule[] modules,
        Assembly[] assemblies,
        IHotUpdateServiceRegistry services)
    {
        Release = release;
        _runtime = runtime;
        _modules = modules;
        _assemblies = assemblies;
        Services = services;
    }

    public ManagedCodeRelease Release { get; }
    public ManagedCodeRuntimeKind RuntimeKind => _runtime.Kind;
    public IHotUpdateServiceRegistry Services { get; }

    public void Dispose() => DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        List<Exception>? failures = null;
        for (var index = _modules.Length - 1; index >= 0; index--)
        {
            try { await _modules[index].StopAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        RuntimeTypeCache.UnregisterAssemblies(_assemblies);
        _assemblies = [];
        _modules = [];
        try { await _runtime.DisposeAsync().ConfigureAwait(false); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException("HotUpdate domain shutdown failed.", failures);
    }
}
