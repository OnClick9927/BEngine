using System.Reflection;
using System.Runtime.Loader;
using BEngine.HotUpdate;

namespace BEngine.Player;

public sealed class CoreClrManagedCodeRuntimeProvider : IManagedCodeRuntimeProvider
{
    public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
    public bool IsAvailable => true;
    public string? UnavailableReason => null;
    public IManagedCodeRuntime CreateRuntime() => new CoreClrManagedCodeRuntime();
}

internal sealed class PlayerManagedCodeRuntimeFactory : IManagedCodeRuntimeFactory
{
    private readonly Dictionary<ManagedCodeRuntimeKind, IManagedCodeRuntimeProvider> _providers;

    internal PlayerManagedCodeRuntimeFactory(IEnumerable<IManagedCodeRuntimeProvider>? providers = null)
    {
        _providers = (providers ?? [])
            .Append(new AotInterpreterManagedCodeRuntimeProvider())
            .Append(new CoreClrManagedCodeRuntimeProvider())
            .GroupBy(provider => provider.Kind)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public IManagedCodeRuntime CreateRuntime(ManagedCodeRuntimeRequest request)
    {
        if (TryCreate(request.PreferredKind, out var runtime, out var reason)) return runtime;
        if (request.AllowFallback && request.PreferredKind != ManagedCodeRuntimeKind.CoreClr &&
            TryCreate(ManagedCodeRuntimeKind.CoreClr, out runtime, out _)) return runtime;
        throw new PlatformNotSupportedException(
            $"Managed-code runtime '{request.PreferredKind}' is unavailable: {reason}");
    }

    private bool TryCreate(
        ManagedCodeRuntimeKind kind,
        out IManagedCodeRuntime runtime,
        out string reason)
    {
        if (!_providers.TryGetValue(kind, out var provider))
        {
            runtime = null!;
            reason = "no runtime provider is registered";
            return false;
        }
        if (!provider.IsAvailable)
        {
            runtime = null!;
            reason = provider.UnavailableReason ?? "the runtime provider reported no reason";
            return false;
        }
        runtime = provider.CreateRuntime();
        reason = string.Empty;
        return true;
    }
}

internal sealed class CoreClrManagedCodeRuntime : IManagedCodeRuntime
{
    private HotUpdateLoadContext? _context;
    private int _disposed;

    public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
    public bool IsLoaded { get; private set; }

    public BValueTask<ManagedCodeLoadResult> LoadAsync(
        ManagedCodeRelease release,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(release);
        if (IsLoaded) throw new InvalidOperationException("The CoreCLR HotUpdate domain is already loaded.");
        release.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var context = new HotUpdateLoadContext(release.Modules);
        try
        {
            var assemblies = new List<Assembly>(release.Modules.Count);
            foreach (var module in OrderModules(release.Modules))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assembly = context.LoadModule(module.Name);
                var actualName = assembly.GetName().Name;
                if (!module.Name.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Managed-code module '{module.Name}' contains assembly '{actualName}'.");
                assemblies.Add(assembly);
            }
            var modules = assemblies.SelectMany(GetTypesStrict)
                .Where(type => type is { IsClass: true, IsAbstract: false } &&
                               (type.IsPublic || type.IsNestedPublic) &&
                               typeof(IHotUpdateModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(type => Activator.CreateInstance(type) as IHotUpdateModule ??
                                throw new InvalidOperationException(
                                    $"HotUpdate module '{type.FullName}' requires a public parameterless constructor."))
                .ToArray();
            _context = context;
            IsLoaded = true;
            return BValueTask<ManagedCodeLoadResult>.FromResult(
                new ManagedCodeLoadResult(modules, assemblies));
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        IsLoaded = false;
        var context = Interlocked.Exchange(ref _context, null);
        context?.Unload();
        return ValueTask.CompletedTask;
    }

    private static ManagedCodeModule[] OrderModules(IReadOnlyList<ManagedCodeModule> modules)
    {
        var byName = modules.ToDictionary(module => module.Name, StringComparer.OrdinalIgnoreCase);
        var states = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<ManagedCodeModule>(modules.Count);
        foreach (var name in byName.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) Visit(name);
        return ordered.ToArray();

        void Visit(string name)
        {
            if (states.TryGetValue(name, out var state))
            {
                if (state == 2) return;
                if (state == 1) throw new InvalidDataException($"Managed-code dependency cycle at '{name}'.");
            }
            var module = byName[name];
            states[name] = 1;
            foreach (var dependency in module.Dependencies) Visit(dependency);
            states[name] = 2;
            ordered.Add(module);
        }
    }

    private static Type[] GetTypesStrict(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            var details = exception.LoaderExceptions
                .Where(error => error is not null)
                .Select(error => error!.Message)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            throw new InvalidDataException(
                $"Managed-code assembly '{assembly.GetName().Name}' could not load all types: " +
                string.Join("; ", details), exception);
        }
    }

    private sealed class HotUpdateLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, ManagedCodeModule> _modules;
        private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

        internal HotUpdateLoadContext(IEnumerable<ManagedCodeModule> modules)
            : base($"BEngine.HotUpdate:{Guid.NewGuid():N}", isCollectible: true) =>
            _modules = modules.ToDictionary(module => module.Name, StringComparer.OrdinalIgnoreCase);

        internal Assembly LoadModule(string name)
        {
            if (_loaded.TryGetValue(name, out var loaded)) return loaded;
            if (!_modules.TryGetValue(name, out var module))
                throw new FileNotFoundException($"Managed-code module '{name}' was not found.");
            using var assemblyStream = new MemoryStream(module.GetAssemblyImage(), writable: false);
            var symbols = module.GetSymbols();
            Assembly assembly;
            if (symbols is { Length: > 0 })
            {
                using var symbolsStream = new MemoryStream(symbols, writable: false);
                assembly = LoadFromStream(assemblyStream, symbolsStream);
            }
            else
            {
                assembly = LoadFromStream(assemblyStream);
            }
            _loaded[name] = assembly;
            return assembly;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (_modules.ContainsKey(name)) return LoadModule(name);
            return Default.Assemblies.FirstOrDefault(assembly =>
                assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
        }
    }
}
