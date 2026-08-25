using System.Reflection;
using System.Runtime.Loader;
using BEngine.Editor;
using BEngine.Rendering;
using BEngine.Serialization;
using BEngine.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BEngine.ProjectSystem;

internal sealed class PackageAssemblyManager : IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly BPackageCatalog _catalog;
    private readonly Dictionary<string, LoadedPackage> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resourceRoots = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingShadowDelete> _pendingShadowDeletes = [];
    private readonly List<Assembly> _pendingReflectionAssemblies = [];
    private readonly string _packageCachePath;
    private bool _dynamicEditorInitialization;

    public PackageAssemblyManager(ProjectWorkspace workspace, BPackageCatalog catalog)
    {
        _workspace = workspace;
        _catalog = catalog;
        _packageCachePath = BEngine.Editor.EditorInstanceContext.current?.packageCachePath ??
                            Path.Combine(_workspace.LibraryPath, "PackageCache");
        CleanupStaleShadowDirectories();
        AssemblyLoadContext.Default.Resolving += ResolveDefaultDependency;
    }

    public bool IsLoaded(string packageId) => _loaded.ContainsKey(packageId);

    public void BeginDynamicEditorInitialization() => _dynamicEditorInitialization = true;

    public void Apply(IEnumerable<BPackageDefinition> enabledDefinitions)
    {
        var enabled = enabledDefinitions.ToDictionary(
            item => item.Document.Id, StringComparer.OrdinalIgnoreCase);
        PackageCompilationResult compilation;
        try
        {
            compilation = PackageSourceCompiler.CompileEnabled(_workspace, enabled.Values,
                (completed, total, item) => EditorUtility.DisplayProgressBar(
                    "编译扩展包", item, total == 0 ? 1f : (float)completed / total));
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        var order = SortByDependencies(enabled.Values).ToArray();
        var allOrder = SortByDependencies(_catalog.packages).Select(item => item.Document.Id).ToArray();
        var unloadedAny = false;

        foreach (var package in _loaded.Keys.Where(id =>
                         !enabled.TryGetValue(id, out var definition) ||
                         !AssemblyPathsMatch(_loaded[id], definition, compilation))
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

        foreach (var definition in order)
        {
            RegisterResourceRoot(definition);
            if (_loaded.ContainsKey(definition.Document.Id)) continue;
            var missingDependency = Dependencies(definition).FirstOrDefault(dependency =>
                enabled.ContainsKey(dependency) && !_loaded.ContainsKey(dependency));
            if (missingDependency is not null)
            {
                EditorFeatureGuard.Report($"Package {definition.Document.Id}",
                    new InvalidOperationException(
                        $"Dependency '{missingDependency}' failed to load; this package was skipped."));
                UnregisterResourceRoot(definition);
                continue;
            }
            if (!EditorFeatureGuard.Invoke($"Package {definition.Document.Id}.Load",
                    () => Load(definition, compilation)))
                UnregisterResourceRoot(definition);
        }

        if (_pendingReflectionAssemblies.Count > 0)
        {
            RuntimeTypeCache.RegisterAssemblies(_pendingReflectionAssemblies);
            _pendingReflectionAssemblies.Clear();
        }

        foreach (var definition in _catalog.packages.Where(item => !enabled.ContainsKey(item.Document.Id)))
            UnregisterResourceRoot(definition);
    }

    public void Dispose()
    {
        AssemblyLoadContext.Default.Resolving -= ResolveDefaultDependency;
        foreach (var packageId in _loaded.Keys.ToArray()) Unload(packageId);
        foreach (var root in _resourceRoots.ToArray())
        {
            Resources.UnregisterResourceRoot(root);
            _resourceRoots.Remove(root);
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        DeletePendingShadowDirectories();
    }

    private Assembly? ResolveDefaultDependency(AssemblyLoadContext _, AssemblyName name) => ResolveDependency(name);

    private void Load(BPackageDefinition definition, PackageCompilationResult compilation)
    {
        var assemblyPaths = new List<string>();
        if (definition.Document.Runtime is { } runtime)
            assemblyPaths.Add(GetCompiledPath(definition, "runtime", runtime.Assembly, compilation));
        if (definition.Document.Editor is { } editor)
            assemblyPaths.Add(GetCompiledPath(definition, "editor", editor.Assembly, compilation));

        var shadowRoot = Path.Combine(_packageCachePath,
            Sanitize(definition.Document.Id), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shadowRoot);
        var context = new PackageLoadContext(definition.Document.Id, shadowRoot, ResolveDependency);
        var assemblies = new List<Assembly>();
        ServiceProvider? services = null;
        var runtimeSystemRegistrations = new List<string>();
        try
        {
            CopyCompanionAssemblies(assemblyPaths, shadowRoot);
            foreach (var sourcePath in assemblyPaths)
            {
                var shadowPath = Path.Combine(shadowRoot, Path.GetFileName(sourcePath));
                assemblies.Add(context.LoadFromAssemblyPath(shadowPath));
            }

            var serviceCollection = new ServiceCollection();
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
            foreach (var systemType in assemblies.SelectMany(GetLoadableTypes)
                         .Where(type => type is { IsClass: true, IsAbstract: false } &&
                                        typeof(ISceneRuntimeSystem).IsAssignableFrom(type)))
            {
                var registrationId = $"{definition.Document.Id}:{systemType.FullName}";
                RuntimeSystemRegistry.RegisterScoped(registrationId, scopeFactory, systemType);
                runtimeSystemRegistrations.Add(registrationId);
            }

            var loaded = new LoadedPackage(definition, context, assemblies.ToArray(),
                assemblyPaths.Select(Path.GetFullPath).ToArray(),
                new WeakReference(context, trackResurrection: false), shadowRoot,
                services, runtimeSystemRegistrations.ToArray());
            _loaded.Add(definition.Document.Id, loaded);
            if (_dynamicEditorInitialization) RuntimeTypeCache.RegisterAssemblies(assemblies);
            else _pendingReflectionAssemblies.AddRange(assemblies);
            if (_dynamicEditorInitialization)
            {
                TypeCache.Refresh();
                SettingsProviderRegistry.Invalidate();
                SettingsProviderRegistry.Warmup();
            }
            if (_dynamicEditorInitialization && definition.Document.Editor is { } editorAssembly)
                EditorInitialization.Run(assemblies.Where(assembly =>
                    assembly.GetName().Name?.Equals(editorAssembly.Assembly,
                        StringComparison.OrdinalIgnoreCase) == true), scriptsReloaded: false);
        }
        catch
        {
            _loaded.Remove(definition.Document.Id);
            _pendingReflectionAssemblies.RemoveAll(assemblies.Contains);
            foreach (var registration in runtimeSystemRegistrations)
                RuntimeSystemRegistry.Unregister(registration);
            foreach (var assembly in assemblies)
            {
                ScenePickingProviderRegistry.UnregisterAssembly(assembly);
                SceneRenderContributor2DRegistry.UnregisterAssembly(assembly);
            }
            services?.Dispose();
            if (_dynamicEditorInitialization && assemblies.Count > 0)
            {
                SettingsProviderRegistry.ForgetAssemblies(assemblies);
                RuntimeTypeCache.UnregisterAssemblies(assemblies);
                PropertyDrawerRegistry.Invalidate();
            }
            context.Unload();
            throw;
        }
    }

    private static void CopyCompanionAssemblies(IEnumerable<string> packageAssemblies, string shadowRoot)
    {
        var sourcePaths = packageAssemblies.Select(Path.GetFullPath).ToArray();
        var directories = sourcePaths.Select(path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories)
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var assemblyName = Path.GetFileNameWithoutExtension(file);
            if (assemblyName is "BEngine" or "BEngine.Editor") continue;
            File.Copy(file, Path.Combine(shadowRoot, Path.GetFileName(file)), true);
            var pdb = Path.ChangeExtension(file, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(shadowRoot, Path.GetFileName(pdb)), true);
        }
        foreach (var sourcePath in sourcePaths)
        {
            File.Copy(sourcePath, Path.Combine(shadowRoot, Path.GetFileName(sourcePath)), true);
            var pdb = Path.ChangeExtension(sourcePath, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.Combine(shadowRoot, Path.GetFileName(pdb)), true);
        }
    }

    private void Unload(string packageId)
    {
        if (!_loaded.Remove(packageId, out var package)) return;
        foreach (var registration in package.RuntimeSystemRegistrations)
            RuntimeSystemRegistry.Unregister(registration);
        package.Services.Dispose();
        EditorInitialization.ForgetAssemblies(package.Assemblies);
        foreach (var assembly in package.Assemblies)
        {
            AssetTypeRegistry.UnregisterAssembly(assembly);
            EditorIconRegistry.UnregisterAssembly(assembly);
            ScenePickingProviderRegistry.UnregisterAssembly(assembly);
            RuntimeSystemRegistry.UnregisterAssembly(assembly);
            SceneRenderContributor2DRegistry.UnregisterAssembly(assembly);
            RemoveStaticEventHandlers(assembly);
        }
        PropertyDrawerRegistry.Invalidate();
        SettingsProviderRegistry.ForgetAssemblies(package.Assemblies);
        RuntimeTypeCache.UnregisterAssemblies(package.Assemblies);
        TypeCache.Refresh();
        package.Context.Unload();
        _pendingShadowDeletes.Add(new PendingShadowDelete(package.ContextReference, package.ShadowRoot));
        UnregisterResourceRoot(package.Definition);
    }

    private Assembly? ResolveDependency(AssemblyName name)
    {
        var simpleName = name.Name;
        if (string.IsNullOrWhiteSpace(simpleName)) return null;
        var packageAssembly = _loaded.Values.SelectMany(item => item.Assemblies).FirstOrDefault(assembly =>
            assembly.GetName().Name?.Equals(simpleName, StringComparison.OrdinalIgnoreCase) == true);
        if (packageAssembly is not null) return packageAssembly;
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            assembly.GetName().Name?.Equals(simpleName, StringComparison.OrdinalIgnoreCase) == true);
    }

    private void RegisterResourceRoot(BPackageDefinition definition)
    {
        var root = PackageRoot(definition);
        if (!_resourceRoots.Add(root)) return;
        Resources.RegisterResourceRoot(root);
    }

    private void UnregisterResourceRoot(BPackageDefinition definition)
    {
        var root = PackageRoot(definition);
        if (!_resourceRoots.Remove(root)) return;
        Resources.UnregisterResourceRoot(root);
    }

    private static string PackageRoot(BPackageDefinition definition) =>
        Path.GetDirectoryName(Path.GetFullPath(definition.Path))!;

    private static string GetCompiledPath(
        BPackageDefinition definition,
        string kind,
        string assemblyName,
        PackageCompilationResult compilation) =>
        compilation.TryGetAssemblyPath(assemblyName, out var path) && File.Exists(path)
            ? Path.GetFullPath(path)
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
            .Select(name => compilation.TryGetAssemblyPath(name!, out var path) ? Path.GetFullPath(path) : null)
            .Where(path => path is not null)
            .Cast<string>()
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return expected.SequenceEqual(
            loaded.AssemblyPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<BPackageDefinition> SortByDependencies(
        IEnumerable<BPackageDefinition> definitions)
    {
        var remaining = definitions.ToDictionary(item => item.Document.Id, StringComparer.OrdinalIgnoreCase);
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (remaining.Count > 0)
        {
            var ready = remaining.Values.Where(definition => Dependencies(definition)
                    .Where(remaining.ContainsKey).All(emitted.Contains))
                .OrderBy(definition => definition.Document.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (ready.Length == 0) throw new InvalidDataException("Package dependency cycle prevents assembly loading.");
            foreach (var definition in ready)
            {
                remaining.Remove(definition.Document.Id);
                emitted.Add(definition.Document.Id);
                yield return definition;
            }
        }
    }

    private static IEnumerable<string> Dependencies(BPackageDefinition definition) =>
        (definition.Document.Runtime?.Dependencies ?? [])
        .Concat(definition.Document.Editor?.Dependencies ?? [])
        .Where(item => !item.Optional && !item.PackageId.Equals(
            definition.Document.Id, StringComparison.OrdinalIgnoreCase))
        .Select(item => item.PackageId).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string Sanitize(string value) => string.Concat(value.Select(character =>
        Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private void DeletePendingShadowDirectories()
    {
        foreach (var pending in _pendingShadowDeletes.ToArray())
        {
            if (pending.ContextReference.IsAlive) continue;
            try
            {
                if (Directory.Exists(pending.Directory)) Directory.Delete(pending.Directory, true);
                _pendingShadowDeletes.Remove(pending);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void CleanupStaleShadowDirectories()
    {
        var packageCache = _packageCachePath;
        if (!Directory.Exists(packageCache)) return;
        foreach (var directory in Directory.EnumerateDirectories(packageCache, "*", SearchOption.TopDirectoryOnly))
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void RemoveStaticEventHandlers(Assembly packageAssembly)
    {
        foreach (var ownerAssembly in new[] { typeof(BObject).Assembly, typeof(EditorApplication).Assembly })
        foreach (var type in GetLoadableTypes(ownerAssembly))
        foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                     .Where(field => typeof(Delegate).IsAssignableFrom(field.FieldType)))
        {
            Delegate? current;
            try { current = field.GetValue(null) as Delegate; }
            catch { continue; }
            if (current is null) continue;
            var remaining = current.GetInvocationList().Where(handler =>
                handler.Method.DeclaringType?.Assembly != packageAssembly &&
                handler.Target?.GetType().Assembly != packageAssembly).ToArray();
            if (remaining.Length == current.GetInvocationList().Length) continue;
            try { field.SetValue(null, remaining.Length == 0 ? null : Delegate.Combine(remaining)); }
            catch { }
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
        PackageLoadContext Context,
        Assembly[] Assemblies,
        string[] AssemblyPaths,
        WeakReference ContextReference,
        string ShadowRoot,
        ServiceProvider Services,
        string[] RuntimeSystemRegistrations);

    private sealed class PackageLoadContext(
        string packageId,
        string shadowRoot,
        Func<AssemblyName, Assembly?> resolveDependency)
        : AssemblyLoadContext($"BEngine.Package:{packageId}", isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var own = Assemblies.FirstOrDefault(assembly => assembly.GetName().Name?.Equals(
                assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true);
            if (own is not null) return own;
            if (resolveDependency(assemblyName) is { } dependency) return dependency;
            var localPath = Path.Combine(shadowRoot, $"{assemblyName.Name}.dll");
            return File.Exists(localPath) ? LoadFromAssemblyPath(localPath) : null;
        }
    }
}
