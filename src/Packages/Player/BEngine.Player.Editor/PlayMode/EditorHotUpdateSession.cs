using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.DependencyInjection;
using BEngine.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor;

internal sealed class EditorHotUpdateSession : IEditorHotUpdateSession
{
    private HotUpdateDomain? _domain;
    private ServiceProvider? _services;
    private IDisposable? _typePreference;
    private Assembly[] _assemblies;
    private BTask? _disposeOperation;
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

    public string ReleaseId { get; }
    public IReadOnlyList<Assembly> Assemblies => Array.AsReadOnly(_assemblies);
    public IServiceProvider Services => _services ??
        throw new ObjectDisposedException(nameof(EditorHotUpdateSession));

    internal static async BValueTask<EditorHotUpdateSession?> CreateAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken = default) =>
        await CreateAsync(
            workspace,
            assetBundles,
            EditorServiceAccess.GetRequiredService<FileUtility>(),
            EditorServiceAccess.GetRequiredService<PathUtility>(),
            EditorServiceAccess.GetRequiredService<PlatformUtility>(),
            EditorServiceAccess.GetRequiredService<ApplicationPathUtility>(),
            EditorServiceAccess.GetRequiredService<IManagedCodeRuntimeFactory>(),
            cancellationToken);

    internal static async BValueTask<EditorHotUpdateSession?> CreateAsync(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths,
        IManagedCodeRuntimeFactory managedCodeRuntimeFactory,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(assetBundles);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(pathService);
        ArgumentNullException.ThrowIfNull(platformService);
        ArgumentNullException.ThrowIfNull(applicationPaths);
        ArgumentNullException.ThrowIfNull(managedCodeRuntimeFactory);
        if (!assetBundles.IsInitialized)
            throw new InvalidOperationException("Editor virtual AssetBundles must be initialized before code injection.");
        var release = await LoadReleaseAsync(assetBundles, cancellationToken);
        if (release is null) return null;

        var target = BEngine.Platform.BuildTargetUtility.Current.InferCurrentDesktop(platformService);
        var capabilities = new List<string>
        {
            "environment:Editor",
            $"platform:{target.Platform}",
            $"architecture:{target.Architecture}",
            $"runtime:{ManagedCodeRuntimeKind.CoreClr}"
        };
        capabilities.AddRange(target.GraphicsBackends.Select(backend => $"graphics:{backend}"));
        var gate = new HotUpdateActivationGate(managedCodeRuntimeFactory, capabilities);
        PreparedHotUpdateDomain? prepared = null;
        IDisposable? preference = null;
        ServiceProvider? playServices = null;
        Assembly[] assemblies = [];
        try
        {
            prepared = await gate.PrepareAsync(
                release,
                new ManagedCodeRuntimeRequest(ManagedCodeRuntimeKind.CoreClr),
                cancellationToken);
            assemblies = prepared.Assemblies.ToArray();
            preference = RuntimeTypeCache.PreferAssemblies(assemblies);
            VerifyPreferredObjectTypes(assemblies, prepared.OwnedAssemblies, release.ReleaseId);
            TypeCache.Refresh();
            playServices = BuildPlayServices(
                workspace, assetBundles, assemblies,
                fileSystem, pathService, platformService, applicationPaths);
            var domain = await prepared.ActivateAsync(playServices, cancellationToken);
            return new EditorHotUpdateSession(domain, playServices, preference, assemblies);
        }
        catch (Exception failure)
        {
            List<Exception>? cleanupFailures = null;
            try { playServices?.Dispose(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            try { preference?.Dispose(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (prepared is not null)
            {
                try { await prepared.DisposeAsync(); }
                catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            }
            try { TypeCache.Refresh(); }
            catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
            if (cleanupFailures is { Count: > 0 })
                throw new AggregateException(
                    "Editor HotUpdate creation and rollback both failed.",
                    [failure, .. cleanupFailures]);
            throw;
        }
    }

    public BValueTask DisposeAsync()
    {
        _disposeOperation ??= DisposeCoreAsync();
        return new BValueTask(_disposeOperation);
    }

    public void Dispose()
    {
        var operation = DisposeAsync().AsBTask();
        if (operation.IsCompleted)
        {
            operation.GetAwaiter().GetResult();
            return;
        }
        _ = ObserveDisposeAsync(operation);
    }

    private async BTask DisposeCoreAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        var domain = Interlocked.Exchange(ref _domain, null);
        var services = Interlocked.Exchange(ref _services, null);
        var typePreference = Interlocked.Exchange(ref _typePreference, null);
        var assemblies = Interlocked.Exchange(ref _assemblies, []);
        List<Exception>? failures = null;
        try
        {
            if (domain is not null) await domain.DisposeAsync();
            else HotUpdateAssemblyCleanup.Unregister(assemblies);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try { typePreference?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        try { TypeCache.Refresh(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        try { services?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 })
            throw new AggregateException("Editor HotUpdate domain shutdown failed.", failures);
    }

    private static async BTask ObserveDisposeAsync(BTask operation)
    {
        try { await operation; }
        catch (Exception exception) { Debug.LogException(exception); }
    }

    private static ServiceProvider BuildPlayServices(
        ProjectWorkspace workspace,
        IAssetBundleManager assetBundles,
        IReadOnlyCollection<Assembly> hotAssemblies,
        FileUtility fileSystem,
        PathUtility pathService,
        PlatformUtility platformService,
        ApplicationPathUtility applicationPaths)
    {
        var services = new ServiceCollection();
        services.ShareBEnginePlatformState(EditorServiceAccess.GetRequiredService<IServiceProvider>());
        services.AddSingleton(fileSystem);
        services.AddSingleton(pathService);
        services.AddSingleton(platformService);
        services.AddSingleton(applicationPaths);
        var runtimeAssemblies = DiscoverRuntimeAssemblies(hotAssemblies);
        services.AddBEngine(
            new EngineServiceContext(
                EngineHostKind.Runtime,
                workspace.RootPath,
                $"EditorPlayMode:{workspace.Project.Name}"),
            runtimeAssemblies);
        services.TryAddSingleton(workspace);
        services.TryAddSingleton(ProjectRuntimeSettings.LoadAndApply(
            workspace, fileSystem, pathService));
        RegisterAssetBundleServices(services, assetBundles, hotAssemblies);
        services.TryAddSingleton<IContentBootstrapper>(new PreparedEditorContentBootstrapper(assetBundles));
        services.TryAddSingleton<ISceneLoader>(new EditorPlayModeSceneLoader(
            workspace, assetBundles, pathService));
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    internal static void RegisterAssetBundleServices(
        IServiceCollection services,
        IAssetBundleManager assetBundles,
        IReadOnlyCollection<Assembly> hotAssemblies)
    {
        services.TryAddSingleton(assetBundles);
        var hostContract = typeof(IAssetBundleManager);
        foreach (var assembly in hotAssemblies)
        {
            if (!string.Equals(assembly.GetName().Name, hostContract.Assembly.GetName().Name,
                    StringComparison.OrdinalIgnoreCase)) continue;
            var playContract = assembly.GetType(hostContract.FullName!, throwOnError: true)!;
            if (playContract == hostContract) continue;
            var managerType = assembly.GetType(typeof(VirtualAssetBundleManager).FullName!, throwOnError: true)!;
            var createBridge = managerType.GetMethod(nameof(VirtualAssetBundleManager.CreateSnapshotBridge),
                BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException(
                    "The Play Mode AssetBundles package must support virtual snapshot bridges. Recompile project packages.");
            // Register under the Play contract inside DI so Scene-created scopes see it
            // and own its lifetime. The host keeps its own contract for scene loading.
            services.AddSingleton(playContract, _ =>
            {
                Func<string, byte[]> read = address => assetBundles.TryLoadBytes(address, out var bytes)
                    ? bytes
                    : throw new FileNotFoundException($"Virtual AssetBundle address '{address}' was not found.", address);
                Func<string, BCancellationToken, BValueTask<(byte[] Bytes, Action Release)>> readAsync =
                    async (address, cancellationToken) =>
                    {
                        var handle = await assetBundles.LoadBytesAsync(address, cancellationToken);
                        return (handle.Value, handle.Dispose);
                    };
                var bridge = createBridge.Invoke(null,
                [
                    AssetBundleCatalogSerializer.SerializeCatalog(assetBundles.ActiveCatalog ??
                        throw new InvalidOperationException("Editor virtual AssetBundles are not initialized.")),
                    AssetBundleCatalogSerializer.SerializeVersion(assetBundles.ActiveVersion ??
                        throw new InvalidOperationException("Editor virtual AssetBundles are not initialized.")),
                    read, readAsync
                ])!;
                try
                {
                    // Virtual initialization completes synchronously and performs no IO.
                    ((BValueTask)playContract.GetMethod(nameof(IAssetBundleManager.InitializeAsync))!
                        .Invoke(bridge, [BCancellationToken.None])!).GetAwaiter().GetResult();
                    return bridge;
                }
                catch
                {
                    ((IDisposable)bridge).Dispose();
                    throw;
                }
            });
        }
    }

    private static Assembly[] DiscoverRuntimeAssemblies(IReadOnlyCollection<Assembly> hotAssemblies)
        => new[] { typeof(BObject).Assembly }.Concat(hotAssemblies).Distinct().ToArray();

    private static void VerifyPreferredObjectTypes(
        IReadOnlyCollection<Assembly> assemblies,
        IReadOnlyList<Assembly> ownedAssemblies,
        string releaseId)
    {
        var releaseAssemblies = assemblies.ToHashSet();
        foreach (var assembly in ownedAssemblies)
        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(BObject).IsAssignableFrom(type) || type.FullName is not { } fullName) continue;
            var active = RuntimeTypeCache.FindType(fullName);
            if (active is not null && releaseAssemblies.Contains(active.Assembly)) continue;
            throw new InvalidDataException(
                $"Play Mode release '{releaseId}' could not activate object type '{fullName}'. " +
                $"Expected {DescribeAssembly(assembly)}; active " +
                $"{(active is null ? "<missing>" : DescribeAssembly(active.Assembly))}. " +
                $"The release assembly index contains {RuntimeTypeCache.GetTypes(assembly).Length} types.");
        }

        static string DescribeAssembly(Assembly assembly) =>
            $"'{assembly.FullName}' (context '{AssemblyLoadContext.GetLoadContext(assembly)?.Name}', " +
            $"MVID {assembly.ManifestModule.ModuleVersionId})";
    }

    private static async BValueTask<ManagedCodeRelease?> LoadReleaseAsync(
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken)
    {
        if (!assetBundles.EnumerateAddresses().Contains(
                ManagedCodeReleaseManifest.DefaultAddress, StringComparer.OrdinalIgnoreCase)) return null;
        using var manifestHandle = await assetBundles.LoadBytesAsync(
            ManagedCodeReleaseManifest.DefaultAddress, cancellationToken);
        var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestHandle.Value);
        var modules = new List<ManagedCodeModule>(manifest.Modules.Count);
        var addresses = assetBundles.EnumerateAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var module in manifest.Modules)
        {
            using var assemblyHandle = await assetBundles.LoadBytesAsync(
                module.AssemblyAddress, cancellationToken);
            byte[] symbols = [];
            if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) && addresses.Contains(module.SymbolsAddress))
            {
                using var symbolsHandle = await assetBundles.LoadBytesAsync(
                    module.SymbolsAddress, cancellationToken);
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
        public BValueTask<ActivatedContentRelease> PrepareAsync(BCancellationToken cancellationToken = default)
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

    internal sealed class EditorManagedCodeRuntimeFactory : IManagedCodeRuntimeFactory
    {
        public EditorManagedCodeRuntimeFactory() { }

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
        private Assembly[] _loadedAssemblies = [];
        private int _disposed;

        public ManagedCodeRuntimeKind Kind => ManagedCodeRuntimeKind.CoreClr;
        public bool IsLoaded { get; private set; }

        public BValueTask<ManagedCodeLoadResult> LoadAsync(
            ManagedCodeRelease release,
            BCancellationToken cancellationToken = default)
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
                    assemblies.Add(assembly);
                    _loadedAssemblies = assemblies.ToArray();
                    if (!module.Name.Equals(assembly.GetName().Name, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(
                            $"Managed-code module '{module.Name}' contains assembly '{assembly.GetName().Name}'.");
                }
                // Reflection alone does not run module initializers. Package codecs and
                // service contributions must belong to this domain before its DI graph is built.
                foreach (var assembly in assemblies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
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
                var ownedAssemblies = assemblies.Where(assembly =>
                    ReferenceEquals(AssemblyLoadContext.GetLoadContext(assembly), context)).ToArray();
                _loadedAssemblies = ownedAssemblies;
                return BValueTask<ManagedCodeLoadResult>.FromResult(
                    new ManagedCodeLoadResult(modules, assemblies, ownedAssemblies));
            }
            catch (Exception loadFailure)
            {
                List<Exception>? cleanupFailures = null;
                try { CleanupLoadedAssemblies(); }
                catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
                try { context.Unload(); }
                catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
                if (cleanupFailures is { Count: > 0 })
                    throw new AggregateException(
                        "Editor HotUpdate load and registration rollback failed.",
                        [loadFailure, .. cleanupFailures]);
                throw;
            }
        }

        public BValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return BValueTask.CompletedTask;
            IsLoaded = false;
            List<Exception>? failures = null;
            try { CleanupLoadedAssemblies(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            try { Interlocked.Exchange(ref _context, null)?.Unload(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            return failures is { Count: > 0 }
                ? BValueTask.FromException(new AggregateException(
                    "Editor HotUpdate runtime disposal failed.", failures))
                : BValueTask.CompletedTask;
        }

        private void CleanupLoadedAssemblies()
        {
            var assemblies = Interlocked.Exchange(ref _loadedAssemblies, []);
            if (assemblies.Length == 0) return;
            List<Exception>? failures = null;
            try { HotUpdateAssemblyCleanup.Unregister(assemblies); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            // The owning session refreshes editor metadata after removing its type
            // preference. Refreshing here repeats the full reflection scan on exit.
            if (failures is { Count: > 0 })
                throw new AggregateException("Editor HotUpdate loaded-assembly cleanup failed.", failures);
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
            var shared = EditorHostServices.Platform.GetLoadedAssemblies().FirstOrDefault(assembly =>
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
