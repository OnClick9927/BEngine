using BEngine.Build;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Hosting;
using BEngine.Player.HotUpdate;

namespace BEngine.Player;

/// <summary>Explicit opt-in to the host's image-loading interpreter capability.</summary>
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
        PlayerHostAssemblies.Initialize();
        return new AotInterpreterManagedCodeRuntime();
    }



    private static bool SupportsManagedAssemblyLoading => AssemblyDomains.Available is { SupportsImageLoading: true };
}

internal sealed class AotInterpreterManagedCodeRuntime : IManagedCodeRuntime
{
    private IAssemblyImageDomain? _context;
    private Assembly[] _loadedAssemblies = [];
    private readonly HashSet<Assembly> _canonicalUses = [];
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
        using (ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.ReleaseValidate)) release.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        // Mono and WebAssembly interpreters execute the IL loaded into the default context. Those runtimes do not
        // provide a portable unload guarantee. A release is loaded once during application bootstrap; replacing it
        // requires restarting the Player process. CoreCLR uses an isolated non-collectible context only so the same
        // byte-loading path can be verified by desktop architecture tests.
        // Registration is the explicit opt-in contract. A trimmed Web runtime
        // need not retain Mono.Runtime, so it must not gate code validation.
        var precompiled = ManagedCodePrecompiledAssemblies.ValidateAndClaim(release, cancellationToken);
        var context = CreateDomain(release.Modules, precompiled, cancellationToken);
        try
        {
            var assemblies = new List<Assembly>(release.Modules.Count);
            IReadOnlyList<ManagedCodeModule> orderedModules;
            using (ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.RuntimeModuleOrder))
                orderedModules = release.OrderedModules;
            foreach (var module in orderedModules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var loadTiming = ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.RuntimeLoadModule);
                var assembly = context.Load(module.Name);
                assemblies.Add(assembly);
                _loadedAssemblies = assemblies.Where(assembly => !BEngine.Hosting.HostAssemblyRegistry.Contains(assembly)).ToArray();
                if (precompiled is not null && !_canonicalUses.Contains(assembly) &&
                    ManagedCodePrecompiledAssemblies.AcquireUse(assembly))
                    _canonicalUses.Add(assembly);
                cancellationToken.ThrowIfCancellationRequested();
                var actualName = assembly.GetName().Name;
                if (!module.Name.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Managed-code module '{module.Name}' contains assembly '{actualName}'.");
            }

            // Mono may defer module constructors when a type is only reached
            // through reflection. Package codecs/services must be registered
            // before game modules begin resolving resources.
            foreach (var assembly in _loadedAssemblies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var initializerTiming = ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.RuntimeModuleInitializers);
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var discoveryTiming = ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.RuntimeModuleDiscovery);
            var modules = _loadedAssemblies.SelectMany(assembly =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var typesTiming = ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.RuntimeGetTypes);
                    return GetTypesStrict(assembly);
                })
                .Where(type => type is { IsClass: true, IsAbstract: false } &&
                               (type.IsPublic || type.IsNestedPublic) &&
                               typeof(IHotUpdateModule).IsAssignableFrom(type))
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .Select(type =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var constructorTiming = ManagedCodeStartupDiagnostics.Measure(ManagedCodeStartupDiagnostics.Phase.RuntimeModuleConstructors);
                    return Activator.CreateInstance(type) as IHotUpdateModule ??
                           throw new InvalidOperationException(
                               $"HotUpdate module '{type.FullName}' requires a public parameterless constructor.");
                })
                .ToArray();
            discoveryTiming.Dispose();
            cancellationToken.ThrowIfCancellationRequested();
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
            finally { context.Dispose(); }
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
        _context?.Dispose();
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
        var uses = _canonicalUses.ToArray();
        _canonicalUses.Clear();
        ManagedCodePrecompiledAssemblies.ReleaseUses(uses);
        HotUpdateAssemblyCleanup.Unregister(assemblies);
    }

    private static Type[] GetTypesStrict(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        {
            var details = ((((global::System.Collections.Generic.IEnumerable<string>)(exception.LoaderExceptions.Where(error => error is not null).Select(error => error!.Message))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.Ordinal)) switch
            {
                (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
                var (distinctItems9622, distinctComparer9622) => distinctItems9622.ToHashSet<string>(distinctComparer9622).ToArray()});
            throw new InvalidDataException(
                $"Managed-code assembly '{assembly.GetName().Name}' could not load all types: " +
                string.Join("; ", details), exception);
        }
    }

    private static IAssemblyImageDomain CreateDomain(IEnumerable<ManagedCodeModule> modules,
        ManagedCodePrecompiledAssemblies.ValidatedRelease? precompiled, BCancellationToken cancellationToken)
    {
        var byName = BuildIndex10286(modules, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule> BuildIndex10286(global::System.Collections.Generic.IEnumerable<global::BEngine.Player.HotUpdate.ManagedCodeModule> indexSource10286, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer10286)
        {
            ArgumentNullException.ThrowIfNull(indexSource10286, "source");
            var knownCount = indexSource10286.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(indexComparer10286);
            global::System.ReadOnlySpan<global::BEngine.Player.HotUpdate.ManagedCodeModule> values;
            if (knownCount && indexSource10286 is global::BEngine.Player.HotUpdate.ManagedCodeModule[] array)
                values = array;
            else if (knownCount && indexSource10286 is global::System.Collections.Generic.List<global::BEngine.Player.HotUpdate.ManagedCodeModule> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(capacity, indexComparer10286);
                foreach (var item in indexSource10286)
                    result.Add(IndexKey10286(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(values.Length, indexComparer10286);
                foreach (var item in values)
                    result.Add(IndexKey10286(item), item);
                return result;
            }
        
            string IndexKey10286(global::BEngine.Player.HotUpdate.ManagedCodeModule module) => module.Name;
        }
        return AssemblyDomains.Current.CreateDomain(new()
        {
            Name = $"InterpretedCode:{Guid.NewGuid():N}",
            Kind = AssemblyImageDomainKind.Interpreter,
            ResolveSharedAssembly = name =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (name.Name is not { } simpleName) return null;
                if (precompiled is not null && precompiled.TryResolve(simpleName, cancellationToken, out var canonical))
                    return canonical;
                return BEngine.Hosting.HostAssemblyRegistry.Find(simpleName);
            },
            ResolveImage = name => name.Name is { } simpleName && byName.TryGetValue(simpleName, out var module)
                ? new AssemblyImage(module.Name, module.GetAssemblyImage(), module.GetSymbols() ?? []) : null
        });
    }
}
