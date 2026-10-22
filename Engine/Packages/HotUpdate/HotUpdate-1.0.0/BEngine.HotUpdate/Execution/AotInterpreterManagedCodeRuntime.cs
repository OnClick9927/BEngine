using BEngine.Build;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BEngine.HotUpdate;

namespace BEngine.Player;

public static class AotInterpreterRuntimeHost
{
    public const string EnabledSwitchName = "BEngine.HotUpdate.AotInterpreter.Enabled";

    public static void DeclareInterpreterEnabled() => AppContext.SetSwitch(EnabledSwitchName, true);

    internal static bool IsDeclaredEnabled =>
        AppContext.TryGetSwitch(EnabledSwitchName, out var enabled) && enabled;
}

public sealed class AotInterpreterManagedCodeRuntimeProvider : IManagedCodeRuntimeProvider
{
    public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.AotInterpreter;

    public bool IsAvailable => AotInterpreterRuntimeHost.IsDeclaredEnabled && SupportsManagedAssemblyLoading;

    public string? UnavailableReason
    {
        get
        {
            if (!AotInterpreterRuntimeHost.IsDeclaredEnabled)
                return $"the platform host did not enable its managed interpreter or declare the " +
                       $"'{AotInterpreterRuntimeHost.EnabledSwitchName}' capability";
            if (!SupportsManagedAssemblyLoading)
                return "the current runtime is NativeAOT (or another runtime without JIT/Mono interpreter " +
                       "assembly loading); NativeAOT cannot load downloaded managed assemblies";
            return null;
        }
    }

    public IManagedCodeRuntime CreateRuntime()
    {
        if (!IsAvailable)
            throw new PlatformNotSupportedException(
                $"The AOT interpreter managed-code runtime is unavailable: {UnavailableReason}");
        return new AotInterpreterManagedCodeRuntime();
    }

    internal static bool IsMonoRuntime => Type.GetType("Mono.Runtime", throwOnError: false) is not null;

    private static bool SupportsManagedAssemblyLoading => RuntimeFeature.IsDynamicCodeSupported || IsMonoRuntime;
}

internal sealed class AotInterpreterManagedCodeRuntime : IManagedCodeRuntime
{
    private InterpreterAssemblyLoadContext? _context;
    private Assembly[] _loadedAssemblies = [];
    private bool _disposed;

    public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.AotInterpreter;
    public bool IsLoaded { get; private set; }

    public BValueTask<ManagedCodeLoadResult> LoadAsync(
        ManagedCodeRelease release,
        BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(release);
        if (IsLoaded) throw new InvalidOperationException("The AOT interpreter HotUpdate domain is already loaded.");
        release.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        // Mono and WebAssembly interpreters execute the IL loaded into the default context. Those runtimes do not
        // provide a portable unload guarantee. A release is loaded once during application bootstrap; replacing it
        // requires restarting the Player process. CoreCLR uses an isolated non-collectible context only so the same
        // byte-loading path can be verified by desktop architecture tests.
        var context = new InterpreterAssemblyLoadContext(release.Modules);
        try
        {
            var assemblies = new List<Assembly>(release.Modules.Count);
            foreach (var module in OrderModules(release.Modules))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assembly = context.LoadModule(module.Name);
                assemblies.Add(assembly);
                _loadedAssemblies = assemblies.Where(assembly => !ManagedCodeHostAssemblies.Contains(assembly)).ToArray();
                var actualName = assembly.GetName().Name;
                if (!module.Name.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Managed-code module '{module.Name}' contains assembly '{actualName}'.");
            }

            // Mono may defer module constructors when a type is only reached
            // through reflection. Package codecs/services must be registered
            // before game modules begin resolving resources.
            foreach (var assembly in _loadedAssemblies)
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            var modules = _loadedAssemblies.SelectMany(GetTypesStrict)
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
                new ManagedCodeLoadResult(modules, assemblies, _loadedAssemblies));
        }
        catch (Exception exception)
        {
            Exception failure = exception is BOperationCanceledException
                ? exception
                : new InvalidDataException(
                    "The platform interpreter could not load the downloaded managed-code release. Verify that the " +
                    "platform host enables its official Mono/WebAssembly interpreter and preserves BEngine contracts.",
                    exception);
            try { CleanupLoadedAssemblies(); }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "AOT interpreter HotUpdate load and registration rollback failed.",
                    failure, cleanupFailure);
            }
            if (ReferenceEquals(failure, exception)) throw;
            throw failure;
        }
    }

    public BValueTask DisposeAsync()
    {
        if (_disposed) return BValueTask.CompletedTask;
        _disposed = true;
        IsLoaded = false;
        Exception? cleanupFailure = null;
        try { CleanupLoadedAssemblies(); }
        catch (Exception exception) { cleanupFailure = exception; }
        _context = null;
        return cleanupFailure is null
            ? BValueTask.CompletedTask
            : BValueTask.FromException(new AggregateException(
                "AOT interpreter HotUpdate runtime disposal failed.", cleanupFailure));
    }

    private void CleanupLoadedAssemblies()
    {
        var assemblies = _loadedAssemblies;
        _loadedAssemblies = [];
        HotUpdateAssemblyCleanup.Unregister(assemblies);
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

    private sealed class InterpreterAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, ManagedCodeModule> _modules;
        private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

        internal InterpreterAssemblyLoadContext(IEnumerable<ManagedCodeModule> modules)
            : base($"BEngine.AotInterpreter:{Guid.NewGuid():N}", isCollectible: false) =>
            _modules = modules.ToDictionary(module => module.Name, StringComparer.OrdinalIgnoreCase);

        internal Assembly LoadModule(string name)
        {
            if (ManagedCodeHostAssemblies.Find(name) is { } hostAssembly) return hostAssembly;
            if (_loaded.TryGetValue(name, out var loaded)) return loaded;
            if (!_modules.TryGetValue(name, out var module))
                throw new FileNotFoundException($"Managed-code module '{name}' was not found.");
            using var assemblyStream = new MemoryStream(module.GetAssemblyImage(), writable: false);
            var symbols = module.GetSymbols();
            Assembly assembly;
            if (AotInterpreterManagedCodeRuntimeProvider.IsMonoRuntime)
            {
                if (Default.Assemblies.Any(candidate =>
                        candidate.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true))
                    throw new InvalidOperationException(
                        $"Managed-code module '{name}' is already loaded. Interpreter releases can only be " +
                        "replaced by restarting the Player process.");
                if (symbols is { Length: > 0 })
                {
                    using var symbolsStream = new MemoryStream(symbols, writable: false);
                    assembly = Default.LoadFromStream(assemblyStream, symbolsStream);
                }
                else
                {
                    assembly = Default.LoadFromStream(assemblyStream);
                }
            }
            else if (symbols is { Length: > 0 })
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
