using System.Reflection;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Serialization;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ProjectSystem;

internal sealed class PackageAssemblyManager : IDisposable
{
    private readonly AssemblyLoadingUtility _assemblyLoading;
    private readonly IDisposable _defaultResolution;
    private readonly PackageHostAssemblyLookup _hostAssemblies;
    private readonly ProjectWorkspace _workspace;
    private readonly BPackageCatalog _catalog;
    private readonly Dictionary<string, LoadedPackage> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Assembly> _loadedAssemblyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resourceRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingShadowDelete> _pendingShadowDeletes = [];
    private readonly List<Assembly> _pendingReflectionAssemblies = [];
    private readonly string _packageCachePath;
    private readonly IPackageSourceCompiler _compiler;
    private bool _dynamicEditorInitialization;
    internal IReadOnlyList<Assembly> Assemblies => _loaded.Values.SelectMany(package => package.Assemblies).ToArray();

    public PackageAssemblyManager(
        ProjectWorkspace workspace,
        BPackageCatalog catalog,
        IPackageSourceCompiler compiler)
    {
        _assemblyLoading = AssemblyLoadingUtility.Current;
        _workspace = workspace;
        _catalog = catalog;
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _packageCachePath = BEngine.Editor.EditorInstanceContext.current?.packageCachePath ??
                            EditorHostServices.Paths.Combine(_workspace.LibraryPath, "PackageCache");
        CleanupStaleShadowDirectories();
        _hostAssemblies = new(_assemblyLoading, EditorHostServices.Platform);
        try { _defaultResolution = _assemblyLoading.ObserveDefaultResolution(ResolveDependency); }
        catch { _hostAssemblies.Dispose(); throw; }
    }

    public bool IsLoaded(string packageId) => _loaded.ContainsKey(packageId);

    public void BeginDynamicEditorInitialization() => _dynamicEditorInitialization = true;

    public void Apply(IEnumerable<BPackageDefinition> enabledDefinitions)
    {
        var enabled = BuildIndex2396(enabledDefinitions, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex2396(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> indexSource2396, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer2396)
        {
            ArgumentNullException.ThrowIfNull(indexSource2396, "source");
            var knownCount = indexSource2396.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer2396);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (knownCount && indexSource2396 is global::BEngine.ProjectSystem.BPackageDefinition[] array)
                values = array;
            else if (knownCount && indexSource2396 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer2396);
                foreach (var item in indexSource2396)
                    result.Add(IndexKey2396(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer2396);
                foreach (var item in values)
                    result.Add(IndexKey2396(item), item);
                return result;
            }
        
            string IndexKey2396(global::BEngine.ProjectSystem.BPackageDefinition item) => item.Document.Id;
        }
        PackageCompilationResult compilation;
        try
        {
            using var measurement = EditorStartupTimings.Measure("Package source compilation");
            compilation = _compiler.CompileEnabled(_workspace, enabled.Values,
                (completed, total, item) => ToolingProgress.Report(
                    "编译扩展包", item, total == 0 ? 1f : (float)completed / total));
        }
        finally
        {
            ToolingProgress.Clear();
        }
        var groups = SortDependencyGroups(enabled.Values);
        var order = groups.SelectMany(group => group.Definitions).ToArray();
        var allOrder = SortByDependencies(_loaded.Values.Select(item => item.Definition)).Select(item => item.Document.Id).ToArray();
        var unloadedAny = false;

        var reload = _loaded.Keys.Where(id =>
                !enabled.TryGetValue(id, out var definition) ||
                !AssemblyPathsMatch(_loaded[id], definition, compilation))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // A retained context can hold direct references to a retired dependency.
        // Reload its complete dependent closure, even when its own PE is unchanged.
        bool expanded;
        do
        {
            expanded = false;
            foreach (var package in _loaded.Values)
                if (Dependencies(package.Definition).Any(reload.Contains))
                    expanded |= reload.Add(package.Definition.Document.Id);
        } while (expanded);
        foreach (var package in reload
                     .OrderByDescending(id => Array.FindIndex(allOrder, candidate =>
                         candidate.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            Unload(package);
            unloadedAny = true;
        }

        if (unloadedAny)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            DeletePendingShadowDirectories();
        }

        var pending = order.Where(definition => !_loaded.ContainsKey(definition.Document.Id))
            .Select(definition => new PackageShadowPreparation.Entry(definition.Document.Id,
                GetAssemblyPaths(definition, compilation), EditorHostServices.Paths.Combine(_packageCachePath,
                    Sanitize(definition.Document.Id), Guid.NewGuid().ToString("N")))).ToArray();
        var shared = _hostAssemblies.GetSimpleNames();
        PackageShadowPreparation preparation;
        try
        {
            using var measurement = EditorStartupTimings.Measure("Package shadow preparation");
            preparation = new(pending, shared, (completed, index) => ToolingProgress.Report(
                "准备扩展包", pending[index].PackageId, pending.Length == 0 ? 1f : (float)completed / pending.Length),
                imageCacheRoot: EditorHostServices.Paths.Combine(_workspace.LibraryPath, "PackageImages"));
        }
        finally { ToolingProgress.Clear(); }
        using var prepared = preparation;
        // The staging map is read concurrently while each dependency wave loads.
        // Clear it after commit/failure so surviving contexts never retain retired ones.
        var stagedAssemblies = new ConcurrentDictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        var additions = order.Where(definition => !_loaded.ContainsKey(definition.Document.Id)).ToArray();
        var committed = new List<string>();
        try
        {
            if (!_dynamicEditorInitialization) PrepareAssemblies(pending, groups, enabled, stagedAssemblies);
            var resolver = CreatePreparationResolver(stagedAssemblies);
            // Prepare every image before executable initialization. Dependencies can
            // span runtime/editor targets without creating a package-level DAG.
            if (_dynamicEditorInitialization)
                foreach (var definition in additions)
                {
                    var entry = prepared[definition.Document.Id];
                    PrepareAssemblyImages(entry, resolver);
                    foreach (var assembly in entry.Assemblies)
                        stagedAssemblies[assembly.GetName().Name!] = assembly;
                }
            foreach (var definition in additions) RegisterResourceRoot(definition);
            var images = BuildIndex6910(additions.SelectMany(definition => prepared[definition.Document.Id].Assemblies), StringComparer.OrdinalIgnoreCase);
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.Dictionary<string, global::System.Reflection.Assembly> BuildIndex6910(global::System.Collections.Generic.IEnumerable<global::System.Reflection.Assembly> indexSource6910, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer6910)
            {
                ArgumentNullException.ThrowIfNull(indexSource6910, "source");
                var knownCount = indexSource6910.TryGetNonEnumeratedCount(out var capacity);
                if (knownCount && capacity == 0)
                    return new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.Assembly>(indexComparer6910);
                global::System.ReadOnlySpan<global::System.Reflection.Assembly> values;
                if (knownCount && indexSource6910 is global::System.Reflection.Assembly[] array)
                    values = array;
                else if (knownCount && indexSource6910 is global::System.Collections.Generic.List<global::System.Reflection.Assembly> list)
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                else
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.Assembly>(capacity, indexComparer6910);
                    foreach (var item in indexSource6910)
                        result.Add(IndexKey6910(item), item);
                    return result;
                }
            
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::System.Reflection.Assembly>(values.Length, indexComparer6910);
                    foreach (var item in values)
                        result.Add(IndexKey6910(item), item);
                    return result;
                }
            
                string IndexKey6910(global::System.Reflection.Assembly assembly) => assembly.GetName().Name!;
            }
            // This is a global phase barrier, not a per-package preference: every
            // runtime target initializes before the first editor target.
            {
                var __projectionSource7266 = SortAssemblyNodes(enabled.Values);
                ArgumentNullException.ThrowIfNull(__projectionSource7266, "source");
                foreach (var __projectionItem7266 in __projectionSource7266)
                {
                    var name = __projectionItem7266.Name;
                    if (images.TryGetValue(name, out var assembly))
                    {
                        using var moduleTiming = EditorStartupTimings.Measure($"Package module: {name}");
                        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
                    }
                }
            }
            foreach (var group in groups)
            {
                var members = BuildIndex8024(group.Definitions);
                [global::BEngine.SkipLoopGuard]
                global::System.Collections.Generic.HashSet<string> BuildIndex8024(global::BEngine.ProjectSystem.BPackageDefinition[] indexSource8024)
                {
                    ArgumentNullException.ThrowIfNull(indexSource8024, "source");
                    var indexComparer8024 = StringComparer.OrdinalIgnoreCase;
                    var result = new global::System.Collections.Generic.HashSet<string>(indexComparer8024);
                    foreach (var definition in indexSource8024)
                        result.Add(definition.Document.Id);
                    return result;
                }
                var groupAdditions = group.Definitions.Where(definition => !_loaded.ContainsKey(definition.Document.Id)).ToArray();
                if (groupAdditions.Length == 0) continue;
                var missingDependency = Matches8387();
                [global::BEngine.SkipLoopGuard]
                string? Matches8387()
                {
                    var matchSource8387 = group.Definitions.SelectMany(Dependencies);
                    ArgumentNullException.ThrowIfNull(matchSource8387, "source");
                    global::System.ReadOnlySpan<string> matchValues8387;
                    if (matchSource8387.GetType() == typeof(string[]))
                        matchValues8387 = (string[])(object)matchSource8387;
                    else if (matchSource8387.GetType() == typeof(global::System.Collections.Generic.List<string>))
                        matchValues8387 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)matchSource8387);
                    else
                    {
                        foreach (var dependency in (global::System.Collections.Generic.IEnumerable<string>)matchSource8387)
                        {
                            if (!members.Contains(dependency) && enabled.ContainsKey(dependency) && !_loaded.ContainsKey(dependency))
                                return dependency;
                        }
                
                        return default!;
                    }
                
                    foreach (var dependency in matchValues8387)
                    {
                        if (!members.Contains(dependency) && enabled.ContainsKey(dependency) && !_loaded.ContainsKey(dependency))
                            return dependency;
                    }
                
                    return default!;
                }
                if (missingDependency is not null)
                    throw new InvalidOperationException(
                        $"Dependency '{missingDependency}' failed to load before package '{groupAdditions[0].Document.Id}'.");
                foreach (var definition in groupAdditions)
                {
                    using var measurement = EditorStartupTimings.Measure($"Package load: {definition.Document.Id}");
                    Load(definition, prepared[definition.Document.Id]);
                    committed.Add(definition.Document.Id);
                }
            }

            if (_pendingReflectionAssemblies.Count > 0)
            {
                using var measurement = EditorStartupTimings.Measure("Package type registration");
                RuntimeTypeCache.RegisterAssemblies(_pendingReflectionAssemblies);
                _pendingReflectionAssemblies.Clear();
            }

            {
                var __filteredSource9264 = _catalog.packages;
                ArgumentNullException.ThrowIfNull(__filteredSource9264, "source");
                foreach (var definition in __filteredSource9264)
                {
                    if (!(!enabled.ContainsKey(definition.Document.Id)))
                        continue;
                    UnregisterResourceRoot(definition);
                }
            }
            foreach (var id in committed) prepared.Retain(id);
        }
        catch (Exception failure)
        {
            // Initializers may register extensions before a provider exists. Roll
            // back every new image in this Apply, while preserving retained packages.
            List<Exception>? failures = null;
            foreach (var id in committed.AsEnumerable().Reverse())
            {
                Try(() => Unload(id));
                prepared[id].Context = null;
                prepared[id].Assemblies = [];
            }
            foreach (var definition in additions)
            {
                Try(() => ForgetAssemblyRegistrations(prepared[definition.Document.Id].Assemblies));
                Try(() => UnregisterResourceRoot(definition));
            }
            if (failures is { Count: > 0 })
                throw new AggregateException("Package activation and rollback failed.", new[] { failure }.Concat(failures));
            throw;
            void Try(Action cleanup)
            {
                try { cleanup(); }
                catch (Exception cleanupFailure) { (failures ??= []).Add(cleanupFailure); }
            }
        }
        finally { stagedAssemblies.Clear(); }
    }

    private void PrepareAssemblies(PackageShadowPreparation.Entry[] entries,
        PackageLoadGroup[] groups, IReadOnlyDictionary<string, BPackageDefinition> enabled,
        ConcurrentDictionary<string, Assembly> stagedAssemblies)
    {
        var remaining = BuildIndex12450(entries, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> BuildIndex12450(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> indexSource12450, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer12450)
        {
            ArgumentNullException.ThrowIfNull(indexSource12450, "source");
            var knownCount = indexSource12450.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry>(indexComparer12450);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> values;
            if (knownCount && indexSource12450 is global::BEngine.ProjectSystem.PackageShadowPreparation.Entry[] array)
                values = array;
            else if (knownCount && indexSource12450 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageShadowPreparation.Entry> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry>(capacity, indexComparer12450);
                foreach (var item in indexSource12450)
                    result.Add(IndexKey12450(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.PackageShadowPreparation.Entry>(values.Length, indexComparer12450);
                foreach (var item in values)
                    result.Add(IndexKey12450(item), item);
                return result;
            }
        
            string IndexKey12450(global::BEngine.ProjectSystem.PackageShadowPreparation.Entry entry) => entry.PackageId;
        }
        var completed = _loaded.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolver = CreatePreparationResolver(stagedAssemblies);
        try
        {
            using var measurement = EditorStartupTimings.Measure("Package PE preparation");
            while (remaining.Count > 0)
            {
                var ready = groups.Where(group => group.Definitions.Any(definition => remaining.ContainsKey(definition.Document.Id)) &&
                        group.ExternalDependencies.Where(enabled.ContainsKey).All(completed.Contains))
                    .SelectMany(group => group.Definitions).Where(definition => remaining.ContainsKey(definition.Document.Id)).ToArray();
                if (ready.Length == 0) throw new InvalidDataException("Package load dependency cycle detected.");
                EditorWorkBatch.Run(ready.Length, index =>
                {
                    var entry = remaining[ready[index].Document.Id];
                    PrepareAssemblyImages(entry, resolver);
                    return index;
                }, (done, index) => ToolingProgress.Report("加载扩展包", ready[index].Document.DisplayName,
                    entries.Length == 0 ? 1f : (float)(entries.Length - remaining.Count + done) / entries.Length),
                    maximumConcurrency: PackageLoadConcurrency());
                foreach (var definition in ready)
                {
                    foreach (var assembly in remaining[definition.Document.Id].Assemblies)
                        stagedAssemblies[assembly.GetName().Name!] = assembly;
                    completed.Add(definition.Document.Id);
                    remaining.Remove(definition.Document.Id);
                }
            }
        }
        finally { ToolingProgress.Clear(); }
    }

    private static int PackageLoadConcurrency()
    {
        var value = EditorHostServices.Platform.GetEnvironmentVariable("BENGINE_EDITOR_PACKAGE_LOAD_WORKERS");
        return int.TryParse(value, out var workers) ? Math.Clamp(workers, 1, 4)
            : Math.Clamp(Environment.ProcessorCount / 2, 1, 2);
    }

    // Keep the lifetime callback in a separate closure. Capturing the preparation
    // method's display class would retain its entire entry/context array.
    private Func<AssemblyName, Assembly?> CreatePreparationResolver(
        ConcurrentDictionary<string, Assembly> stagedAssemblies) =>
        name => stagedAssemblies.TryGetValue(name.Name ?? "", out var staged) ? staged : ResolveDependency(name);

    private void PrepareAssemblyImages(PackageShadowPreparation.Entry entry, Func<AssemblyName, Assembly?> resolver)
    {
        using var timing = EditorStartupTimings.Measure($"Package PE load: {entry.PackageId}");
        var context = entry.Context = _assemblyLoading.CreatePackageScope(entry.PackageId, entry.ImageRoot, resolver);
        if (entry.ImageLease is { } lease)
        {
            PackageImageCache.RetainUntilCollected(context.Lifetime, lease);
            entry.ImageLease = null;
        }
        entry.Assemblies = entry.AssemblyPaths.Select(source =>
        {
            var name = _assemblyLoading.GetAssemblyName(source).Name;
            var hosted = _hostAssemblies.Find(name, ignoreCase: false);
            return hosted ?? context.LoadFromAssemblyPath(EditorHostServices.Paths.Combine(entry.ImageRoot,
                EditorHostServices.Paths.GetFileName(source)));
        }).ToArray();
    }

    public void Dispose()
    {
        List<Exception>? failures = null;
        Try(_defaultResolution.Dispose);
        foreach (var packageId in _loaded.Keys.ToArray()) Try(() => Unload(packageId));
        foreach (var root in _resourceRoots.ToArray())
        {
            Try(() => Resources.UnregisterResourceRoot(root));
            _resourceRoots.Remove(root);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Try(DeletePendingShadowDirectories);
        Try(_hostAssemblies.Dispose);
        if (failures is { Count: > 0 }) throw new AggregateException("Package scope cleanup failed.", failures);
        void Try(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }


    private static string[] GetAssemblyPaths(BPackageDefinition definition, PackageCompilationResult compilation)
    {
        var assemblyPaths = new List<string>();
        if (definition.Document.Runtime is { } runtime)
            assemblyPaths.Add(GetCompiledPath(definition, "runtime", runtime.Assembly, compilation));
        if (definition.Document.Editor is { } editor)
            assemblyPaths.Add(GetCompiledPath(definition, "editor", editor.Assembly, compilation));

        return assemblyPaths.Select(EditorHostServices.Paths.GetFullPath).ToArray();
    }

    private void Load(BPackageDefinition definition, PackageShadowPreparation.Entry prepared)
    {
        var assemblyPaths = prepared.AssemblyPaths;
        var shadowRoot = prepared.ShadowRoot;
        var context = prepared.Context ?? throw new InvalidOperationException("Package assemblies were not prepared.");
        var assemblies = prepared.Assemblies;
        ServiceProvider? services = null;
        var runtimeSystemRegistrations = new List<string>();
        try
        {
            using (EditorStartupTimings.Measure($"Package services: {definition.Document.Id}"))
            {
                var serviceCollection = new ServiceCollection();
                serviceCollection.ShareBEnginePlatformState(EditorServiceAccess.GetRequiredService<IServiceProvider>());
                serviceCollection.AddBEngine(
                    new EngineServiceContext(EngineHostKind.Editor, _workspace.RootPath,
                        $"Package:{definition.Document.Id}"), assemblies);
                serviceCollection.AddSingleton(_workspace);
                serviceCollection.AddSingleton(definition);
                services = serviceCollection.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
                var scopeFactory = services.GetRequiredService<IServiceScopeFactory>();
                {
                    var __filteredSource16693 = assemblies.SelectMany(GetLoadableTypes);
                    ArgumentNullException.ThrowIfNull(__filteredSource16693, "source");
                    foreach (var systemType in __filteredSource16693)
                    {
                        if (!(systemType is { IsClass: true, IsAbstract: false } && typeof(ISceneRuntimeSystem).IsAssignableFrom(systemType)))
                            continue;
                        var registrationId = $"{definition.Document.Id}:{systemType.FullName}";
                        RuntimeSystemRegistry.RegisterScoped(registrationId, scopeFactory, systemType);
                        runtimeSystemRegistrations.Add(registrationId);
                    }
                }
            }

            var loaded = new LoadedPackage(definition, context, assemblies.ToArray(),
                assemblyPaths.Select(EditorHostServices.Paths.GetFullPath).ToArray(),
                context.Lifetime, shadowRoot,
                services, runtimeSystemRegistrations.ToArray());
            _loaded.Add(definition.Document.Id, loaded);
            foreach (var assembly in assemblies)
                _loadedAssemblyNames[assembly.GetName().Name!] = assembly;
            if (_dynamicEditorInitialization) RuntimeTypeCache.RegisterAssemblies(assemblies);
            else _pendingReflectionAssemblies.AddRange(assemblies);
            if (_dynamicEditorInitialization)
            {
                EditorExtensionLifecycle.SynchronizeReflection();
                SettingsProviderRegistry.Invalidate();
                SettingsProviderRegistry.Warmup();
            }
            if (_dynamicEditorInitialization && definition.Document.Editor is { } editorAssembly)
                EditorInitialization.Run(assemblies.Where(assembly =>
                    assembly.GetName().Name?.Equals(editorAssembly.Assembly,
                        StringComparison.OrdinalIgnoreCase) == true), scriptsReloaded: false);
        }
        catch (Exception failure)
        {
            List<Exception>? failures = null;
            _loaded.Remove(definition.Document.Id);
            ForgetAssemblyNames(assemblies);
            _pendingReflectionAssemblies.RemoveAll(assemblies.Contains);
            foreach (var registration in runtimeSystemRegistrations)
                Try(() => RuntimeSystemRegistry.Unregister(registration));
            Try(() => ForgetAssemblyRegistrations(assemblies));
            Try(() => services?.Dispose());
            if (_dynamicEditorInitialization && assemblies.Length > 0)
            {
                Try(() => SettingsProviderRegistry.ForgetAssemblies(assemblies.Where(_assemblyLoading.IsCollectible)));
                Try(() => RuntimeTypeCache.UnregisterAssemblies(assemblies.Where(_assemblyLoading.IsCollectible)));
                Try(EditorExtensionLifecycle.Invalidate);
            }
            // The preparation owner unloads any entry that was not committed.
            if (failures is { Count: > 0 })
                throw new AggregateException("Package load and rollback failed.", new[] { failure }.Concat(failures));
            throw;
            void Try(Action cleanup)
            {
                try { cleanup(); }
                catch (Exception cleanupFailure) { (failures ??= []).Add(cleanupFailure); }
            }
        }
    }

    private void Unload(string packageId)
    {
        if (!_loaded.Remove(packageId, out var package)) return;
        List<Exception>? failures = null;
        ForgetAssemblyNames(package.Assemblies);
        _pendingReflectionAssemblies.RemoveAll(package.Assemblies.Contains);
        Try(BAsset.ClearLoadedAssets);
        Try(EditorExtensionLifecycle.Invalidate);
        foreach (var registration in package.RuntimeSystemRegistrations)
            Try(() => RuntimeSystemRegistry.Unregister(registration));
        Try(package.Services.Dispose);
        var retiring = package.Assemblies.Where(_assemblyLoading.IsCollectible).ToArray();
        Try(() => EditorInitialization.ForgetAssemblies(retiring));
        Try(() => ForgetAssemblyRegistrations(package.Assemblies));
        Try(EditorExtensionLifecycle.Invalidate);
        Try(() => SettingsProviderRegistry.ForgetAssemblies(retiring));
        Try(() => RuntimeTypeCache.UnregisterAssemblies(retiring));
        Try(EditorExtensionLifecycle.SynchronizeReflection);
        Try(package.Context.Unload);
        _pendingShadowDeletes.Add(new PendingShadowDelete(package.ContextReference, package.ShadowRoot));
        Try(() => UnregisterResourceRoot(package.Definition));
        if (failures is { Count: > 0 }) throw new AggregateException($"Package '{packageId}' cleanup failed.", failures);
        void Try(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }

    private void ForgetAssemblyRegistrations(IEnumerable<Assembly> assemblies)
    {
        List<Exception>? failures = null;
        foreach (var assembly in assemblies.Where(_assemblyLoading.IsCollectible))
        {
            Try(() => RuntimeAssemblyCleanup.UnregisterExtensions(assembly));
            Try(() => RuntimeSystemRegistry.UnregisterAssembly(assembly));
            Try(() => RuntimeAssetCodecRegistry.UnregisterAssembly(assembly));
            Try(() => YamlUtility.UnregisterConverters(assembly));
            Try(() => RuntimeServiceContributions.UnregisterAssembly(assembly));
            Try(() => RemoveStaticEventHandlers(assembly));
        }
        if (failures is { Count: > 0 }) throw new AggregateException("Package registration cleanup failed.", failures);
        void Try(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception failure) { (failures ??= []).Add(failure); }
        }
    }

    private Assembly? ResolveDependency(AssemblyName name)
    {
        var simpleName = name.Name;
        if (string.IsNullOrWhiteSpace(simpleName)) return null;
        return _loadedAssemblyNames.TryGetValue(simpleName, out var packageAssembly)
            ? packageAssembly : _hostAssemblies.Find(simpleName, ignoreCase: true);
    }

    private void ForgetAssemblyNames(IEnumerable<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var name = assembly.GetName().Name;
            if (name is not null && _loadedAssemblyNames.TryGetValue(name, out var indexed) && ReferenceEquals(indexed, assembly))
                _loadedAssemblyNames.TryRemove(name, out _);
        }
    }

    private IEnumerable<string> ResourceRoots(BPackageDefinition definition)
    {
        var root = PackageRoot(definition);
        yield return root;
        if (!string.IsNullOrWhiteSpace(definition.Document.Content.Editor))
            yield return EditorHostServices.Paths.Combine(root, definition.Document.Content.Editor);
    }
    private void RegisterResourceRoot(BPackageDefinition definition)
    {
        foreach (var root in ResourceRoots(definition))
            if (_resourceRoots.Add(root)) Resources.RegisterResourceRoot(root);
    }
    private void UnregisterResourceRoot(BPackageDefinition definition)
    {
        foreach (var root in ResourceRoots(definition))
            if (_resourceRoots.Remove(root)) Resources.UnregisterResourceRoot(root);
    }

    private static string PackageRoot(BPackageDefinition definition) =>
        EditorHostServices.Paths.GetDirectoryName(EditorHostServices.Paths.GetFullPath(definition.Path))!;

    private static string GetCompiledPath(
        BPackageDefinition definition,
        string kind,
        string assemblyName,
        PackageCompilationResult compilation) =>
        compilation.TryGetAssemblyPath(assemblyName, out var path) && EditorHostServices.FileSystem.FileExists(path)
            ? EditorHostServices.Paths.GetFullPath(path)
            : throw new FileNotFoundException(
                $"Package '{definition.Document.Id}' {kind} source did not produce assembly " +
                $"'{assemblyName}'.", path);

    private static bool AssemblyPathsMatch(
        LoadedPackage loaded,
        BPackageDefinition definition,
        PackageCompilationResult compilation)
    {
        var expected = new[]
            {
                definition.Document.Runtime?.Assembly,
                definition.Document.Editor?.Assembly
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => compilation.TryGetAssemblyPath(name!, out var path) ? EditorHostServices.Paths.GetFullPath(path) : null)
            .Where(path => path is not null)
            .Cast<string>()
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return expected.SequenceEqual(
            loaded.AssemblyPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<BPackageDefinition> SortByDependencies(
        IEnumerable<BPackageDefinition> definitions) =>
        SortDependencyGroups(definitions).SelectMany(group => group.Definitions);

    private static PackageLoadGroup[] SortDependencyGroups(IEnumerable<BPackageDefinition> definitions)
    {
        var packages = BuildIndex27213(definitions, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex27213(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> indexSource27213, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer27213)
        {
            ArgumentNullException.ThrowIfNull(indexSource27213, "source");
            var knownCount = indexSource27213.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer27213);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (knownCount && indexSource27213 is global::BEngine.ProjectSystem.BPackageDefinition[] array)
                values = array;
            else if (knownCount && indexSource27213 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer27213);
                foreach (var item in indexSource27213)
                    result.Add(IndexKey27213(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer27213);
                foreach (var item in values)
                    result.Add(IndexKey27213(item), item);
                return result;
            }
        
            string IndexKey27213(global::BEngine.ProjectSystem.BPackageDefinition item) => item.Document.Id;
        }
        var assemblyOrder = SortAssemblyNodes(packages.Values);

        // Condense package SCCs. Every member's images and module initializers are
        // available before services are constructed, so cross-target dependencies
        // work without inventing an order between mutually dependent packages.
        var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        var components = new List<PackageLoadGroup>();
        var nextIndex = 0;
        foreach (var id in packages.Keys.Order(StringComparer.OrdinalIgnoreCase))
            if (!indices.ContainsKey(id)) Visit(id);

        void Visit(string id)
        {
            indices[id] = lowLinks[id] = nextIndex++;
            stack.Push(id);
            active.Add(id);
            foreach (var dependency in Dependencies(packages[id]).Where(packages.ContainsKey)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                if (!indices.ContainsKey(dependency))
                {
                    Visit(dependency);
                    lowLinks[id] = Math.Min(lowLinks[id], lowLinks[dependency]);
                }
                else if (active.Contains(dependency))
                    lowLinks[id] = Math.Min(lowLinks[id], indices[dependency]);
            }
            if (lowLinks[id] != indices[id]) return;
            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string member;
            do
            {
                member = stack.Pop();
                active.Remove(member);
                members.Add(member);
            } while (!member.Equals(id, StringComparison.OrdinalIgnoreCase));
            var images = assemblyOrder.Where(node => members.Contains(node.PackageId)).ToArray();
            var ordered = members.Select(memberId => packages[memberId])
                .OrderBy(definition => Array.FindLastIndex(images, node =>
                    node.PackageId.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase)))
                .ThenBy(definition => definition.Document.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            components.Add(new PackageLoadGroup(ordered,
                ((((global::System.Collections.Generic.IEnumerable<string>)(ordered.SelectMany(Dependencies).Where(dependency => !members.Contains(dependency)))), (global::System.Collections.Generic.IEqualityComparer<string>? )(StringComparer.OrdinalIgnoreCase)) switch
                {
                    (string[] { Length: 0 }, _) => global::System.Array.Empty<string>(),
                    var (distinctItems38546, distinctComparer38546) => distinctItems38546.ToHashSet<string>(distinctComparer38546).ToArray()})));
        }

        var remaining = components.ToList();
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PackageLoadGroup>();
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(group => group.ExternalDependencies
                    .Where(packages.ContainsKey).All(emitted.Contains))
                .OrderBy(group => group.Definitions[0].Document.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            if (ready.Length == 0) throw new InvalidDataException("Package dependency cycle prevents assembly loading.");
            foreach (var group in ready)
            {
                remaining.Remove(group);
                foreach (var definition in group.Definitions) emitted.Add(definition.Document.Id);
                result.Add(group);
            }
        }
        return result.ToArray();
    }

    private static AssemblyLoadNode[] SortAssemblyNodes(IEnumerable<BPackageDefinition> definitions)
    {
        var packages = BuildIndex30999(definitions, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex30999(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> indexSource30999, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer30999)
        {
            ArgumentNullException.ThrowIfNull(indexSource30999, "source");
            var knownCount = indexSource30999.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer30999);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (knownCount && indexSource30999 is global::BEngine.ProjectSystem.BPackageDefinition[] array)
                values = array;
            else if (knownCount && indexSource30999 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer30999);
                foreach (var item in indexSource30999)
                    result.Add(IndexKey30999(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer30999);
                foreach (var item in values)
                    result.Add(IndexKey30999(item), item);
                return result;
            }
        
            string IndexKey30999(global::BEngine.ProjectSystem.BPackageDefinition item) => item.Document.Id;
        }
        // Validate the same runtime/editor targets as compilation and the catalog.
        // Collapsing those targets to packages can introduce artificial cycles.
        var nodes = new Dictionary<string, AssemblyLoadNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in packages.Values)
        foreach (var (kind, assembly) in new[]
                 { ("runtime", definition.Document.Runtime), ("editor", definition.Document.Editor) })
        {
            if (assembly is null) continue;
            var dependencies = assembly.Dependencies.Where(dependency => packages.ContainsKey(dependency.PackageId))
                .Select(dependency => dependency.PackageId + "\0" + dependency.Target).ToArray();
            nodes.Add(definition.Document.Id + "\0" + kind,
                new AssemblyLoadNode(definition.Document.Id, kind, assembly.Assembly, dependencies));
        }
        var assemblyOrder = new List<AssemblyLoadNode>();
        var remainingNodes = new Dictionary<string, AssemblyLoadNode>(nodes, StringComparer.OrdinalIgnoreCase);
        var emittedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (remainingNodes.Count > 0)
        {
            var phase = Query43491(remainingNodes.Values) ? "runtime" : "editor";
            [global::BEngine.SkipLoopGuard]
            bool Query43491(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode> querySource43491)
            {
                ArgumentNullException.ThrowIfNull(querySource43491, "source");
                bool Matches43491(global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode node) => node.Kind == "runtime";
                global::System.ReadOnlySpan<global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode> queryValues43491;
                if (querySource43491.GetType() == typeof(global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode[]))
                    queryValues43491 = (global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode[])querySource43491;
                else if (querySource43491.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode>))
                    queryValues43491 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.ProjectSystem.PackageAssemblyManager.AssemblyLoadNode>)querySource43491);
                else
                {
                    foreach (var node in querySource43491)
                        if (Matches43491(node))
                            return true;
                    return false;
                }
            
                foreach (var node in queryValues43491)
                    if (Matches43491(node))
                        return true;
                return false;
            }
            var ready = remainingNodes.Where(item => item.Value.Kind == phase && item.Value.Dependencies
                    .Where(nodes.ContainsKey).All(emittedNodes.Contains))
                .OrderBy(item => item.Value.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (ready.Length == 0)
                throw new InvalidDataException("Package assembly dependency cycle prevents assembly loading.");
            foreach (var (key, node) in ready)
            {
                remainingNodes.Remove(key);
                emittedNodes.Add(key);
                assemblyOrder.Add(node);
            }
        }

        return assemblyOrder.ToArray();
    }

    private sealed record AssemblyLoadNode(string PackageId, string Kind, string Name, string[] Dependencies);
    private sealed record PackageLoadGroup(BPackageDefinition[] Definitions,
        string[] ExternalDependencies);

    private static IEnumerable<string> Dependencies(BPackageDefinition definition) =>
        (definition.Document.Runtime?.Dependencies ?? [])
        .Concat(definition.Document.Editor?.Dependencies ?? [])
        .Where(item => !item.PackageId.Equals(
            definition.Document.Id, StringComparison.OrdinalIgnoreCase))
        .Select(item => item.PackageId).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        EditorHostServices.Paths.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private void DeletePendingShadowDirectories()
    {
        foreach (var pending in _pendingShadowDeletes.ToArray())
        {
            if (pending.ContextReference.IsAlive) continue;
            try
            {
                if (EditorHostServices.FileSystem.DirectoryExists(pending.Directory)) EditorHostServices.FileSystem.DeleteDirectory(pending.Directory, true);
                _pendingShadowDeletes.Remove(pending);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void CleanupStaleShadowDirectories()
    {
        var packageCache = _packageCachePath;
        if (!EditorHostServices.FileSystem.DirectoryExists(packageCache)) return;
        foreach (var directory in EditorHostServices.FileSystem.EnumerateDirectories(packageCache, "*", SearchOption.TopDirectoryOnly))
        {
            try { EditorHostServices.FileSystem.DeleteDirectory(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RemoveStaticEventHandlers(Assembly packageAssembly)
    {
        foreach (var ownerAssembly in RuntimeTypeCache.GetAllTypes().Select(type => type.Assembly)
                     .Append(typeof(BObject).Assembly).Append(typeof(EditorServices).Assembly).Distinct())
        foreach (var type in GetLoadableTypes(ownerAssembly))
        {
            var __filteredSource33258 = type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            ArgumentNullException.ThrowIfNull(__filteredSource33258, "source");
            foreach (var field in __filteredSource33258)
            {
                if (!(typeof(Delegate).IsAssignableFrom(field.FieldType)))
                    continue;
                Delegate? current;
                try
                {
                    current = field.GetValue(null) as Delegate;
                }
                catch
                {
                    continue;
                }
        
                if (current is null)
                    continue;
                var handlers = current.GetInvocationList();
                var remaining = handlers.Where(handler => handler.Method.DeclaringType?.Assembly != packageAssembly && handler.Target?.GetType().Assembly != packageAssembly).ToArray();
                if (remaining.Length == handlers.Length)
                    continue;
                try
                {
                    field.SetValue(null, remaining.Length == 0 ? null : Delegate.Combine(remaining));
                }
                catch
                {
                }
            }
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception)
        { return exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
    }

    private sealed record PendingShadowDelete(WeakReference ContextReference, string Directory);

    private sealed record LoadedPackage(
        BPackageDefinition Definition,
        AssemblyScope Context,
        Assembly[] Assemblies,
        string[] AssemblyPaths,
        WeakReference ContextReference,
        string ShadowRoot,
        ServiceProvider Services,
        string[] RuntimeSystemRegistrations);

}
