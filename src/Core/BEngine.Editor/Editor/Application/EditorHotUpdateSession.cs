using System.Reflection;
using System.Runtime.Loader;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.DependencyInjection;
using BEngine.HotUpdate;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

internal sealed class EditorHotUpdateSession : IDisposable
{
    private HotUpdateDomain? _domain;
    private ServiceProvider? _services;
    private IDisposable? _typePreference;
    private Assembly[] _assemblies;
    private int _disposed;

    private EditorHotUpdateSession(
        HotUpdateDomain domain,
        ServiceProvider services,
        IDisposable typePreference,
        Assembly[] assemblies)
    {
        _domain = domain;
        _services = services;
        _typePreference = typePreference;
        _assemblies = assemblies;
        ReleaseId = domain.Release.ReleaseId;
    }

    internal string ReleaseId { get; }
    internal IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);
    internal IServiceProvider Services => _services ??
        throw new ObjectDisposedException(nameof(EditorHotUpdateSession));

    internal static async ValueTask<EditorHotUpdateSession?> CreateAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetBundles);
        if (!assetBundles.IsInitialized)
            throw new InvalidOperationException("Editor virtual AssetBundles must be initialized before code injection.");
        var release = await LoadReleaseAsync(assetBundles, cancellationToken).ConfigureAwait(false);
        if (release is null) return null;

        var target = BuildTargetCatalog.InferCurrentDesktop();
        var capabilities = new List<string>
        {
            "environment:Editor",
            $"platform:{target.Platform}",
            $"architecture:{target.Architecture}",
            $"runtime:{ManagedCodeRuntimeKind.CoreClr}"
        };
        capabilities.AddRange(target.GraphicsBackends.Select(backend => $"graphics:{backend}"));
        var gate = new HotUpdateActivationGate(new EditorManagedCodeRuntimeFactory(), capabilities);
        PreparedHotUpdateDomain? prepared = null;
        IDisposable? preference = null;
        ServiceProvider? playServices = null;
        Assembly[] assemblies = [];
        try
        {
            prepared = await gate.PrepareAsync(
                release,
                new ManagedCodeRuntimeRequest(ManagedCodeRuntimeKind.CoreClr),
                cancellationToken).ConfigureAwait(false);
            assemblies = prepared.Assemblies.ToArray();
            preference = RuntimeTypeCache.PreferAssemblies(assemblies);
            TypeCache.Refresh();
            playServices = BuildPlayServices(workspace, assetBundles, assemblies);
            var domain = await prepared.ActivateAsync(playServices, cancellationToken).ConfigureAwait(false);
            return new EditorHotUpdateSession(domain, playServices, preference, assemblies);
        }
        catch
        {
            playServices?.Dispose();
            preference?.Dispose();
            CleanupRuntimeRegistrations(assemblies);
            RuntimeTypeCache.UnregisterAssemblies(assemblies);
            foreach (var assembly in assemblies) BEngine.Serialization.SceneAssetSerialization.UnregisterAssembly(assembly);
            TypeCache.Refresh();
            if (prepared is not null) await prepared.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Exception? failure = null;
        var domain = Interlocked.Exchange(ref _domain, null);
        try { domain?.Dispose(); }
        catch (Exception exception) { failure = exception; }

        var services = Interlocked.Exchange(ref _services, null);
        try { services?.Dispose(); }
        catch (Exception exception) { failure = failure is null ? exception : new AggregateException(failure, exception); }

        Interlocked.Exchange(ref _typePreference, null)?.Dispose();
        var assemblies = Interlocked.Exchange(ref _assemblies, []);
        CleanupRuntimeRegistrations(assemblies);
        RuntimeTypeCache.UnregisterAssemblies(assemblies);
        foreach (var assembly in assemblies) BEngine.Serialization.SceneAssetSerialization.UnregisterAssembly(assembly);
        TypeCache.Refresh();
        if (failure is not null) throw failure;
    }

    private static void CleanupRuntimeRegistrations(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            RuntimeSystemRegistry.UnregisterAssembly(assembly);
            RuntimeAssetCodecRegistry.UnregisterAssembly(assembly);
            SceneRenderContributor2DRegistry.UnregisterAssembly(assembly);
            RemoveCoreEventHandlers(assembly);
        }
    }

    private static void RemoveCoreEventHandlers(Assembly hotAssembly)
    {
        foreach (var type in GetLoadableTypes(typeof(BObject).Assembly))
        foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                     .Where(field => typeof(Delegate).IsAssignableFrom(field.FieldType)))
        {
            Delegate? current;
            try { current = field.GetValue(null) as Delegate; }
            catch { continue; }
            if (current is null) continue;
            var invocationList = current.GetInvocationList();
            var remaining = invocationList.Where(handler =>
                handler.Method.DeclaringType?.Assembly != hotAssembly &&
                handler.Target?.GetType().Assembly != hotAssembly).ToArray();
            if (remaining.Length == invocationList.Length) continue;
            try { field.SetValue(null, remaining.Length == 0 ? null : Delegate.Combine(remaining)); }
            catch { }
        }
    }

    private static ServiceProvider BuildPlayServices(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        IReadOnlyCollection<Assembly> hotAssemblies)
    {
        var services = new ServiceCollection();
        var runtimeAssemblies = DiscoverRuntimeAssemblies(hotAssemblies);
        services.AddBEngine(
            new EngineServiceContext(
                EngineHostKind.Runtime,
                workspace.RootPath,
                $"EditorPlayMode:{workspace.Project.Name}"),
            runtimeAssemblies);
        services.TryAddSingleton(workspace);
        services.TryAddSingleton(ProjectRuntimeSettings.LoadAndApply(workspace));
        services.TryAddSingleton<IAssetBundleManager>(assetBundles);
        services.TryAddSingleton<IContentBootstrapper>(new PreparedEditorContentBootstrapper(assetBundles));
        services.TryAddSingleton<ISceneLoader>(new EditorPlayModeSceneLoader(workspace, assetBundles));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static Assembly[] DiscoverRuntimeAssemblies(IReadOnlyCollection<Assembly> hotAssemblies)
        => new[] { typeof(BObject).Assembly }.Concat(hotAssemblies).Distinct().ToArray();

    private static async ValueTask<ManagedCodeRelease?> LoadReleaseAsync(
        IAssetBundleManager assetBundles,
        CancellationToken cancellationToken)
    {
        if (!assetBundles.EnumerateAddresses().Contains(
                ManagedCodeReleaseManifest.DefaultAddress, StringComparer.OrdinalIgnoreCase)) return null;
        await using var manifestHandle = await assetBundles.LoadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress, cancellationToken).ConfigureAwait(false);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        var addresses = assetBundles.EnumerateAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            await using var assemblyHandle = await assetBundles.LoadBytesAsync(
                module.AssemblyAddress, cancellationToken).ConfigureAwait(false);
            byte[] symbols = [];
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) && addresses.Contains(module.SymbolsAddress))
            {
                await using var symbolsHandle = await assetBundles.LoadBytesAsync(
                    module.SymbolsAddress, cancellationToken).ConfigureAwait(false);
                symbols = (byte[])symbolsHandle.Value.Clone();
            }
            modules.Add(new ManagedCodeModule(
                module.Name,
                module.BuildId,
                assemblyHandle.Value,
                symbols,
                module.Dependencies));
        }
        return new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
    }

    private sealed class PreparedEditorContentBootstrapper(IAssetBundleManager assetBundles) : IContentBootstrapper
    {
        public BValueTask<ActivatedContentRelease> PrepareAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!assetBundles.IsInitialized || assetBundles.ActiveVersion is null)
                throw new InvalidOperationException("Editor virtual AssetBundles are not initialized.");
            return BValueTask<ActivatedContentRelease>.FromResult(new ActivatedContentRelease(
                assetBundles.ActiveVersion.Version,
                ContentEnvironmentKind.EditorVirtual,
                assetBundles,
                false));
        }
    }

    private sealed class EditorManagedCodeRuntimeFactory : IManagedCodeRuntimeFactory
    {
        public IManagedCodeRuntime CreateRuntime(ManagedCodeRuntimeRequest request)
        {
            if (request.PreferredKind != ManagedCodeRuntimeKind.CoreClr)
                throw new PlatformNotSupportedException(
                    $"The Editor host supports {ManagedCodeRuntimeKind.CoreClr}, not {request.PreferredKind}.");
            return new EditorCoreClrManagedCodeRuntime();
        }
    }

    private sealed class EditorCoreClrManagedCodeRuntime : IManagedCodeRuntime
    {
        private EditorHotUpdateLoadContext? _context;
        private int _disposed;

        public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
        public bool IsLoaded { get; private set; }

        public BValueTask<ManagedCodeLoadResult> LoadAsync(
            ManagedCodeRelease release,
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (IsLoaded) throw new InvalidOperationException("The Editor HotUpdate domain is already loaded.");
            release.Validate();
            var context = new EditorHotUpdateLoadContext(release.Modules);
            try
            {
                var assemblies = new List<Assembly>(release.Modules.Count);
                foreach (var module in OrderModules(release.Modules))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var assembly = context.LoadModule(module.Name);
                    if (!module.Name.Equals(assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"Managed-code module '{module.Name}' contains assembly '{assembly.GetName().Name}'.");
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
            Interlocked.Exchange(ref _context, null)?.Unload();
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
                states[name] = 1;
                var module = byName[name];
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
                var details = exception.LoaderExceptions.Where(error => error is not null)
                    .Select(error => error!.Message).Distinct(StringComparer.Ordinal);
                throw new InvalidDataException(
                    $"Managed-code assembly '{assembly.GetName().Name}' could not load all types: " +
                    string.Join("; ", details), exception);
            }
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        { return exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
        catch { return []; }
    }

    private sealed class EditorHotUpdateLoadContext : AssemblyLoadContext
    {
        private readonly Dictionary<string, ManagedCodeModule> _modules;
        private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

        internal EditorHotUpdateLoadContext(IEnumerable<ManagedCodeModule> modules)
            : base($"BEngine.Editor.HotUpdate:{Guid.NewGuid():N}", isCollectible: true) =>
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
            else assembly = LoadFromStream(assemblyStream);
            _loaded.Add(name, assembly);
            return assembly;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (_modules.ContainsKey(name)) return LoadModule(name);
            var shared = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true);
            if (shared is null || ReferenceEquals(shared, typeof(BObject).Assembly)) return shared;
            var coreName = typeof(BObject).Assembly.GetName().Name;
            try
            {
                if (shared.GetReferencedAssemblies().Any(reference => reference.Name == coreName))
                    throw new FileLoadException(
                        $"Managed-code dependency '{name}' references BEngine but is absent from the HotUpdate release. " +
                        "Runtime engine extensions must be packaged as managed-code modules.");
            }
            catch (NotSupportedException) { }
            return shared;
        }
    }
}
