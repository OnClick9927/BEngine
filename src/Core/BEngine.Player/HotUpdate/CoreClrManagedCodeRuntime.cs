using BEngine.Build;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Hosting;
using BEngine.Player.HotUpdate;

namespace BEngine.Player;

/// <summary>Managed-code strategy over the host's assembly-domain capability.</summary>
public sealed class CoreClrManagedCodeRuntimeProvider : IManagedCodeRuntimeProvider
{
    public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
    public bool IsAvailable => AssemblyDomains.Available is { SupportsImageLoading: true };
    public string? UnavailableReason => IsAvailable ? null : "the platform host does not support managed assembly image loading";
    public IManagedCodeRuntime CreateRuntime()
    {
        PlayerHostAssemblies.Initialize();
        return new CoreClrManagedCodeRuntime();
    }
}

public sealed class PlayerManagedCodeRuntimeFactory : IManagedCodeRuntimeFactory
{
    private readonly Dictionary<ManagedCodeRuntimeKind, IManagedCodeRuntimeProvider> _providers;

    public PlayerManagedCodeRuntimeFactory(IEnumerable<IManagedCodeRuntimeProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers
            .GroupBy(provider => provider.Kind)
            .ToDictionary(group => group.Key, SelectProvider);
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

    private static IManagedCodeRuntimeProvider SelectProvider(
        IGrouping<ManagedCodeRuntimeKind, IManagedCodeRuntimeProvider> providers)
    {
        var values = providers.ToArray();
        return Scan2610(values) ?? values.Last();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Player.HotUpdate.IManagedCodeRuntimeProvider? Scan2610(global::System.Collections.Generic.IEnumerable<global::BEngine.Player.HotUpdate.IManagedCodeRuntimeProvider> scanSource2610)
        {
            ArgumentNullException.ThrowIfNull(scanSource2610, "source");
            if (scanSource2610 is global::System.Collections.Generic.IList<global::BEngine.Player.HotUpdate.IManagedCodeRuntimeProvider> scanList2610)
            {
                for (var scanIndex2610 = scanList2610.Count - 1; scanIndex2610 >= 0; scanIndex2610--)
                {
                    var scanItem2610 = scanList2610[scanIndex2610];
                    if (Match2610(scanItem2610))
                        return scanItem2610;
                }
        
                return default!;
            }
        
            global::BEngine.Player.HotUpdate.IManagedCodeRuntimeProvider? result = default!;
            foreach (var scanItem2610 in scanSource2610)
                if (Match2610(scanItem2610))
                    result = scanItem2610;
            return result;
            bool Match2610(global::BEngine.Player.HotUpdate.IManagedCodeRuntimeProvider provider) => provider is not CoreClrManagedCodeRuntimeProvider and not AotInterpreterManagedCodeRuntimeProvider;
        }
    }
}

internal sealed class CoreClrManagedCodeRuntime : IManagedCodeRuntime
{
    private IAssemblyImageDomain? _context;
    private Assembly[] _loadedAssemblies = [];
    private bool _disposed;

    public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
    public bool IsLoaded { get; private set; }

    public BValueTask<ManagedCodeLoadResult> LoadAsync(
        ManagedCodeRelease release,
        BCancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(release);
        if (IsLoaded) throw new InvalidOperationException("The CoreCLR HotUpdate domain is already loaded.");
        release.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var context = CreateDomain(release.Modules);
        try
        {
            var assemblies = new List<Assembly>(release.Modules.Count);
            foreach (var module in release.OrderedModules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var assembly = context.Load(module.Name);
                assemblies.Add(assembly);
                _loadedAssemblies = assemblies.Where(assembly => !BEngine.Hosting.HostAssemblyRegistry.Contains(assembly)).ToArray();
                var actualName = assembly.GetName().Name;
                if (!module.Name.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $"Managed-code module '{module.Name}' contains assembly '{actualName}'.");
            }
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
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            try { CleanupLoadedAssemblies(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            try { context.Dispose(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (cleanupFailures is { Count: > 0 })
                throw new AggregateException(
                    "CoreCLR HotUpdate load and registration rollback failed.",
                    [failure, .. cleanupFailures]);
            throw;
        }
    }

    public BValueTask DisposeAsync()
    {
        if (_disposed) return BValueTask.CompletedTask;
        _disposed = true;
        IsLoaded = false;
        List<Exception>? failures = null;
        try { CleanupLoadedAssemblies(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        var context = _context;
        _context = null;
        try { context?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        return failures is { Count: > 0 }
            ? BValueTask.FromException(new AggregateException(
                "CoreCLR HotUpdate runtime disposal failed.", failures))
            : BValueTask.CompletedTask;
    }

    private void CleanupLoadedAssemblies()
    {
        var assemblies = _loadedAssemblies;
        _loadedAssemblies = [];
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
                var (distinctItems7296, distinctComparer7296) => distinctItems7296.ToHashSet<string>(distinctComparer7296).ToArray()});
            throw new InvalidDataException(
                $"Managed-code assembly '{assembly.GetName().Name}' could not load all types: " +
                string.Join("; ", details), exception);
        }
    }

    private static IAssemblyImageDomain CreateDomain(IEnumerable<ManagedCodeModule> modules)
    {
        var byName = BuildIndex7849(modules, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule> BuildIndex7849(global::System.Collections.Generic.IEnumerable<global::BEngine.Player.HotUpdate.ManagedCodeModule> indexSource7849, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer7849)
        {
            ArgumentNullException.ThrowIfNull(indexSource7849, "source");
            var knownCount = indexSource7849.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(indexComparer7849);
            global::System.ReadOnlySpan<global::BEngine.Player.HotUpdate.ManagedCodeModule> values;
            if (knownCount && indexSource7849 is global::BEngine.Player.HotUpdate.ManagedCodeModule[] array)
                values = array;
            else if (knownCount && indexSource7849 is global::System.Collections.Generic.List<global::BEngine.Player.HotUpdate.ManagedCodeModule> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(capacity, indexComparer7849);
                foreach (var item in indexSource7849)
                    result.Add(IndexKey7849(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(values.Length, indexComparer7849);
                foreach (var item in values)
                    result.Add(IndexKey7849(item), item);
                return result;
            }
        
            string IndexKey7849(global::BEngine.Player.HotUpdate.ManagedCodeModule module) => module.Name;
        }
        return AssemblyDomains.Current.CreateDomain(new()
        {
            Name = $"ManagedCode:{Guid.NewGuid():N}",
            Kind = AssemblyImageDomainKind.Collectible,
            ResolveSharedAssembly = name => name.Name is { } simpleName ? BEngine.Hosting.HostAssemblyRegistry.Find(simpleName) : null,
            ResolveImage = name => name.Name is { } simpleName && byName.TryGetValue(simpleName, out var module)
                ? new AssemblyImage(module.Name, module.GetAssemblyImage(), module.GetSymbols() ?? []) : null
        });
    }
}
