using EditorCapabilities = BEngine.Editor.EditorServices;
using System.Reflection;
using System.Runtime.CompilerServices;
using BEngine.Hosting;
using BEngine.AssetBundles;
using BEngine.Build;
using BEngine.Content;
using BEngine.DependencyInjection;
using BEngine.Player.HotUpdate;
using BEngine.Platform;
using BEngine.ProjectSystem;
using BEngine.Rendering;
using BEngine.SceneManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BEngine.Editor.Build;

internal sealed class EditorHotUpdateSession : IEditorHotUpdateSession
{
    private HotUpdateDomain? _domain;
    private ServiceProvider? _services;
    private IDisposable? _typePreference;
    private Assembly[] _assemblies;
    private BTask? _disposeOperation;
    private int _disposed;

    private EditorHotUpdateSession(
        HotUpdateDomain? domain,
        ServiceProvider services,
        IDisposable? typePreference,
        Assembly[] assemblies)
    {
        _domain = domain;
        _services = services;
        _typePreference = typePreference;
        _assemblies = assemblies;
        ReleaseId = domain?.Release.ReleaseId ?? "engine-only";
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
            EditorCapabilities.GetRequiredService<FileUtility>(),
            EditorCapabilities.GetRequiredService<PathUtility>(),
            EditorCapabilities.GetRequiredService<PlatformUtility>(),
            EditorCapabilities.GetRequiredService<ApplicationPathUtility>(),
            EditorCapabilities.GetRequiredService<IManagedCodeRuntimeFactory>(),
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
            throw new InvalidOperationException("Editor AssetBundles must be initialized before code injection.");
        var release = await LoadReleaseAsync(assetBundles, cancellationToken);
        if (release is null)
            return new EditorHotUpdateSession(null,
                BuildPlayServices(workspace, assetBundles, [], fileSystem, pathService, platformService, applicationPaths),
                null, []);

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
            EditorExtensionLifecycle.SynchronizeReflection();
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
            try { EditorExtensionLifecycle.SynchronizeReflection(); }
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
            else ManagedCodeServices.ReleaseRegistrations(assemblies);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }

        try { typePreference?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }

        try { EditorExtensionLifecycle.SynchronizeReflection(); }
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
        services.ShareBEnginePlatformState(EditorCapabilities.GetRequiredService<IServiceProvider>());
        services.AddSingleton(fileSystem);
        services.AddSingleton(pathService);
        services.AddSingleton(platformService);
        services.AddSingleton(EditorCapabilities.GetRequiredService<IApplicationPaths>());
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
            workspace, fileSystem, pathService).ToRuntime());
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
            var managerType = assembly.GetType(typeof(AssetBundleContentBridge).FullName!, throwOnError: true)!;
            var createBridge = managerType.GetMethod(nameof(AssetBundleContentBridge.Create),
                BindingFlags.Public | BindingFlags.Static) ??
                throw new InvalidOperationException(
                    "The Play Mode AssetBundles package must support content bridges. Recompile project packages.");
            // Register under the Play contract inside DI so Scene-created scopes see it
            // and own its lifetime. The host keeps its own contract for scene loading.
            services.AddSingleton(playContract, _ =>
            {
                Func<string, byte[]> read = address => assetBundles.TryLoadBytes(address, out var bytes)
                    ? bytes
                    : throw new FileNotFoundException($"Editor AssetBundle address '{address}' was not found.", address);
                Func<string, BCancellationToken, BValueTask<(byte[] Bytes, Action Release)>> readAsync =
                    async (address, cancellationToken) =>
                    {
                        var handle = await assetBundles.LoadBytesAsync(address, cancellationToken);
                        return (handle.Value, handle.Dispose);
                    };
                var catalogBytes = AssetBundleCatalogSerializer.SerializeCatalog(assetBundles.ActiveCatalog ??
                    throw new InvalidOperationException("Editor AssetBundles are not initialized."));
                var sourceVersion = assetBundles.ActiveVersion ??
                    throw new InvalidOperationException("Editor AssetBundles are not initialized.");
                // The cross-domain metadata snapshot uses YAML even when the physical release uses
                // a compiled catalog. Its version must describe these exact bridge bytes.
                var bridgeVersion = new AssetBundleVersion
                {
                    PackageName = sourceVersion.PackageName,
                    Version = sourceVersion.Version,
                    CatalogFile = "catalog.yaml",
                    CatalogSize = catalogBytes.LongLength,
                    CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(catalogBytes)
                };
                var bridge = createBridge.Invoke(null,
                [
                    catalogBytes,
                    AssetBundleCatalogSerializer.SerializeVersion(bridgeVersion),
                    read, readAsync
                ])!;
                try
                {
                    // Bridge metadata initialization completes synchronously; payload IO stays in the host manager.
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
            $"'{assembly.FullName}' (context '{AssemblyDomains.Current.GetDomainName(assembly)}', " +
            $"MVID {assembly.ManifestModule.ModuleVersionId})";
    }

    private static async BValueTask<ManagedCodeRelease?> LoadReleaseAsync(
        IAssetBundleManager assetBundles,
        BCancellationToken cancellationToken)
    {
        if (!assetBundles.EnumerateAddresses().Contains(
                ManagedCodeReleaseManifest.DefaultAddress, StringComparer.OrdinalIgnoreCase)) return null;
        var addresses = assetBundles.EnumerateAddresses().ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var operation = new ManagedCodeReleaseLoadOperation(async (address, context) =>
        {
            var handle = await assetBundles.LoadBytesAsync(address, context.cancellationToken);
            return new ManagedCodeResource(handle.Value, handle);
        }, resourceExists: addresses.Contains, cancellationToken: cancellationToken);
        await operation;
        return operation.release!;
    }

    private sealed class PreparedEditorContentBootstrapper(IAssetBundleManager assetBundles) : IContentBootstrapper
    {
        public BValueTask<ActivatedContentRelease> PrepareAsync(BCancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!assetBundles.IsInitialized || assetBundles.ActiveVersion is null)
                throw new InvalidOperationException("Editor AssetBundles are not initialized.");
            return BValueTask<ActivatedContentRelease>.FromResult(new ActivatedContentRelease(
                assetBundles.ActiveVersion.Version,
                assetBundles is AssetBundleManager ? ContentEnvironmentKind.EditorReal : ContentEnvironmentKind.EditorVirtual,
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
        private IAssemblyImageDomain? _context;
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
            var context = CreateDomain(release.Modules);
            try
            {
                var assemblies = new List<Assembly>(release.Modules.Count);
                foreach (var module in OrderModules(release.Modules))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var assembly = context.Load(module.Name);
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
                var ownedAssemblies = assemblies.Where(context.OwnsAssembly).ToArray();
                _loadedAssemblies = ownedAssemblies;
                return BValueTask<ManagedCodeLoadResult>.FromResult(
                    new ManagedCodeLoadResult(modules, assemblies, ownedAssemblies));
            }
            catch (Exception loadFailure)
            {
                List<Exception>? cleanupFailures = null;
                try { CleanupLoadedAssemblies(); }
                catch (Exception exception) { (cleanupFailures ??= []).Add(exception); }
                try { context.Dispose(); }
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
            try { Interlocked.Exchange(ref _context, null)?.Dispose(); }
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
            try { ManagedCodeServices.ReleaseRegistrations(assemblies); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
            // The owning session refreshes editor metadata after removing its type
            // preference. Refreshing here repeats the full reflection scan on exit.
            if (failures is { Count: > 0 })
                throw new AggregateException("Editor HotUpdate loaded-assembly cleanup failed.", failures);
        }

        private static ManagedCodeModule[] OrderModules(IReadOnlyList<ManagedCodeModule> modules) =>
            ManagedCodeServices.OrderModules(modules, static module => module.Name, static module => module.Dependencies);

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

    private static IAssemblyImageDomain CreateDomain(IEnumerable<ManagedCodeModule> modules)
    {
        var byName = BuildIndex23505(modules, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule> BuildIndex23505(global::System.Collections.Generic.IEnumerable<global::BEngine.Player.HotUpdate.ManagedCodeModule> indexSource23505, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer23505)
        {
            ArgumentNullException.ThrowIfNull(indexSource23505, "source");
            var knownCount = indexSource23505.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(indexComparer23505);
            global::System.ReadOnlySpan<global::BEngine.Player.HotUpdate.ManagedCodeModule> values;
            if (knownCount && indexSource23505 is global::BEngine.Player.HotUpdate.ManagedCodeModule[] array)
                values = array;
            else if (knownCount && indexSource23505 is global::System.Collections.Generic.List<global::BEngine.Player.HotUpdate.ManagedCodeModule> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(capacity, indexComparer23505);
                foreach (var item in indexSource23505)
                    result.Add(IndexKey23505(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Player.HotUpdate.ManagedCodeModule>(values.Length, indexComparer23505);
                foreach (var item in values)
                    result.Add(IndexKey23505(item), item);
                return result;
            }
        
            string IndexKey23505(global::BEngine.Player.HotUpdate.ManagedCodeModule module) => module.Name;
        }
        return AssemblyDomains.Current.CreateDomain(new()
        {
            Name = $"EditorManagedCode:{Guid.NewGuid():N}",
            Kind = AssemblyImageDomainKind.Collectible,
            ResolveImage = name => name.Name is { } simpleName && byName.TryGetValue(simpleName, out var module)
                ? new AssemblyImage(module.Name, module.GetAssemblyImage(), module.GetSymbols() ?? []) : null,
            ResolveSharedAssembly = assemblyName =>
            {
                var name = assemblyName.Name;
                if (string.IsNullOrWhiteSpace(name)) return null;
                if (BEngine.Hosting.HostAssemblyRegistry.Find(name) is { } host) return host;
                if (byName.ContainsKey(name)) return null;
                var shared = Matches24365();
                [global::BEngine.SkipLoopGuard]
                global::System.Reflection.Assembly? Matches24365()
                {
                    var matchSource24365 = EditorCapabilities.GetRequiredService<PlatformUtility>().GetLoadedAssemblies();
                    ArgumentNullException.ThrowIfNull(matchSource24365, "source");
                    global::System.ReadOnlySpan<global::System.Reflection.Assembly> matchValues24365;
                    if (matchSource24365.GetType() == typeof(global::System.Reflection.Assembly[]))
                        matchValues24365 = (global::System.Reflection.Assembly[])(object)matchSource24365;
                    else if (matchSource24365.GetType() == typeof(global::System.Collections.Generic.List<global::System.Reflection.Assembly>))
                        matchValues24365 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::System.Reflection.Assembly>)(object)matchSource24365);
                    else
                    {
                        foreach (var assembly in (global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly>)matchSource24365)
                        {
                            if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                                return assembly;
                        }
                
                        return default!;
                    }
                
                    foreach (var assembly in matchValues24365)
                    {
                        if (assembly.GetName().Name?.Equals(name, StringComparison.OrdinalIgnoreCase) == true)
                            return assembly;
                    }
                
                    return default!;
                }
                if (shared is null || ReferenceEquals(shared, typeof(BObject).Assembly)) return shared;
                var coreName = typeof(BObject).Assembly.GetName().Name;
                try
                {
                    bool __queryResult24790;
                    {
                        var __querySource24790 = shared.GetReferencedAssemblies();
                        ArgumentNullException.ThrowIfNull(__querySource24790, "source");
                        __queryResult24790 = false;
                        foreach (var __queryItem24790 in __querySource24790)
                        {
                            if ((__queryItem24790.Name == coreName))
                            {
                                __queryResult24790 = true;
                                break;
                            }
                        }
                    }
                    
                    if (__queryResult24790)
                        throw new FileLoadException($"Managed-code dependency '{name}' references BEngine but is absent from the HotUpdate release. " + "Runtime engine extensions must be packaged as managed-code modules.");
                }
                catch (NotSupportedException) { }
                return shared;
            }
        });
    }
}
