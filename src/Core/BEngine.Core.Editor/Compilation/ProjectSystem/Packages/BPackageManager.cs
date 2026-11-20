using BEngine.Serialization;
using BEngine.Editor.Documents;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

public sealed class BPackageManager : IDisposable
{
    private static readonly HashSet<string> BuiltInModulePackageIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.bengine.attributes", "com.bengine.math", "com.bengine.async",
        "com.bengine.build-core", "com.bengine.collections", "com.bengine.serialization",
        "com.bengine.serialization-core", "com.bengine.instrumentation",
        "com.bengine.foundation", "com.bengine.compilation", "com.bengine.editor-services",
        "com.bengine.asset-pipeline", "com.bengine.player", "com.bengine.player-build",
        "com.bengine.pooling", "com.bengine.coroutines",
        // Former platform packages are supplied by the selected host. Keep the
        // historical IDs explicit so missing third-party packages still fail validation.
        "com.bengine.platform-abstractions", "com.bengine.platform-contracts",
        "com.bengine.platform-build", "com.bengine.platform-dot-net", "com.bengine.platform-desktop",
        "com.bengine.platform-veldrid", "com.bengine.platform-windows", "com.bengine.platform-windows-build",
        "com.bengine.platform-linux", "com.bengine.platform-linux-build",
        "com.bengine.platformmac-os", "com.bengine.platformmac-osbuild",
        "com.bengine.platform-android", "com.bengine.platform-android-build",
        "com.bengine.platformi-os", "com.bengine.platformi-osbuild",
        "com.bengine.platform-web", "com.bengine.platform-web-build"
    };

    private readonly ProjectWorkspace _workspace;
    internal ProjectWorkspace Workspace => _workspace;
    private BPackageCatalog _catalog;
    private readonly bool _discoverRepository;
    private BPackageCatalog _resolutionCatalog;
    private readonly ProjectPackageCache _cache;
    private readonly PackageAssemblyManager? _assemblyManager;
    private IReadOnlyDictionary<string, BPackageDefinition> _activeDefinitions =
        new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase);
    private PackageManifestDocument _manifest;
    private BPackageCatalog? _previewResolution;
    private BPackageDefinition? _previewDefinition;
    private IReadOnlyList<BPackageDefinition> _previewDependencies = [];
    private BPackageCatalog? _requiredSourceCatalog;
    private BPackageCatalog? _requiredResolutionCatalog;
    private IReadOnlyDictionary<string, IReadOnlyList<BPackageDefinition>> _requiredBy =
        new Dictionary<string, IReadOnlyList<BPackageDefinition>>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _requiredIds = [];
    private BPackageCatalog? _replacementCatalog;
    private IReadOnlyDictionary<string, string> _replacementOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private BPackageCatalog? _viewCatalog;
    private IReadOnlyDictionary<string, BPackageDefinition>? _viewActiveDefinitions;
    private IReadOnlyList<BPackageDefinition> _definitionView = [];
    private readonly Dictionary<string, IReadOnlyList<BPackageDefinition>> _versionViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _viewGate = new();
    private PackageViewMetadata[] _viewMetadata = [];
    private PackageViewMetadata[] _replacementMetadata = [];

    public event Action? packagesChanged;
    internal IReadOnlyList<System.Reflection.Assembly> LoadedAssemblies => _assemblyManager?.Assemblies ?? [];
    public event Action? packagesReloading;
    public event Action<IReadOnlyList<BPackageDefinition>>? packagesUnloading;
    public IReadOnlyList<PackageReferenceDocument> packages => _manifest.Packages;
    public IReadOnlyList<BPackageDefinition> definitions
    {
        get
        {
            lock (_viewGate)
            {
                EnsurePackageViews();
                return _definitionView;
            }
        }
    }

    private void EnsurePackageViews()
    {
        if (ReferenceEquals(_viewCatalog, _catalog) && ReferenceEquals(_viewActiveDefinitions, _activeDefinitions) &&
            MetadataMatches(_viewMetadata)) return;
        RebuildPackageViews();
    }

    // Repository reloads and transactional package application replace these snapshots.
    // Keep rebuilding work separate so the cache-hit path creates no LINQ closure.
    private void RebuildPackageViews()
    {
        var retired = ReplacementOwners();
        var definitions = _catalog.packages
            .Where(definition => !retired.ContainsKey(definition.Document.Id))
            .Select(definition => _activeDefinitions.GetValueOrDefault(definition.Document.Id, definition))
            .Concat(_activeDefinitions.Values.Where(definition =>
                !retired.ContainsKey(definition.Document.Id) && !_catalog.TryGet(definition.Document.Id, out _)))
            .OrderBy(definition => definition.Document.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        _definitionView = Array.AsReadOnly(definitions);
        _versionViews.Clear();
        _viewMetadata = _catalog.EnumerateReleases()
            .Concat(_activeDefinitions.Values).Select(PackageViewMetadata.Capture).ToArray();
        _viewCatalog = _catalog;
        _viewActiveDefinitions = _activeDefinitions;
    }

    public BPackageManager(ProjectWorkspace workspace) : this(workspace, null) { }

    public BPackageManager(ProjectWorkspace workspace, BPackageCatalog? catalog)
        : this(workspace, catalog, loadAssemblies: true)
    {
    }

    /// <summary>Opens package resolution and manifest editing without compiling or loading
    /// package assemblies. Intended for project creation and other authoring transactions.</summary>
    public static BPackageManager OpenManifest(ProjectWorkspace workspace, BPackageCatalog? catalog = null) =>
        new(workspace, catalog, loadAssemblies: false);

    internal BPackageManager(ProjectWorkspace workspace, BPackageCatalog? catalog, bool loadAssemblies)
        : this(workspace, catalog, loadAssemblies, null)
    {
    }

    internal BPackageManager(
        ProjectWorkspace workspace,
        BPackageCatalog? catalog,
        bool loadAssemblies,
        IPackageSourceCompiler? packageSourceCompiler)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        if (loadAssemblies) EditorStartupTimings.Mark("Package preparation and loading");
        _discoverRepository = catalog is null;
        _catalog = catalog ?? BPackageCatalog.Discover(workspace);
        _cache = new ProjectPackageCache(workspace);
        _manifest = LoadOrCreate();
        _resolutionCatalog = ResolveInstalledCatalog();
        NormalizeManifest();
        _assemblyManager = loadAssemblies
            ? new PackageAssemblyManager(
                workspace,
                _catalog,
                packageSourceCompiler ?? new PackageSourceCompilerService(ProcessUtility.Current))
            : null;
        try { ApplyRuntimePackageState(); }
        catch (Exception failure)
        {
            try { _assemblyManager?.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Package activation and cleanup failed.", failure, cleanup); }
            throw;
        }
    }

    public bool IsEnabled(string packageId)
    {
        for (var i = 0; i < _manifest.Packages.Count; i++)
        {
            var package = _manifest.Packages[i];
            if (package.Enabled && package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>True for required roots and their nonoptional selected-version dependencies.</summary>
    public bool IsRequired(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        EnsureRequiredPackagePolicy();
        return _requiredBy.ContainsKey(packageId);
    }

    /// <summary>Required roots that make a package mandatory, excluding the package itself.</summary>
    public IReadOnlyList<BPackageDefinition> GetRequiredBy(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        EnsureRequiredPackagePolicy();
        return _requiredBy.TryGetValue(packageId, out var roots)
            ? roots.Where(root => !root.Document.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
    }

    public IReadOnlyList<string> GetRequiredPackageIds()
    {
        EnsureRequiredPackagePolicy();
        return _requiredIds;
    }

    public bool IsLoaded(string packageId) => _assemblyManager?.IsLoaded(packageId) == true;

    public void BeginDynamicEditorInitialization() => _assemblyManager?.BeginDynamicEditorInitialization();

    public bool TryGetDefinition(string packageId, out BPackageDefinition definition)
    {
        if (IsRetiredPackage(packageId))
        {
            definition = null!;
            return false;
        }
        return _activeDefinitions.TryGetValue(packageId, out definition!) ||
               _catalog.TryGet(packageId, out definition!);
    }

    public IReadOnlyList<BPackageDefinition> GetDependencies(string packageId) =>
        _resolutionCatalog.GetDependencyClosure(packageId).Select(_resolutionCatalog.GetRequired).ToArray();

    public IReadOnlyList<BPackageDefinition> GetDependents(string packageId) =>
        _resolutionCatalog.GetDependentClosure(packageId).Select(_resolutionCatalog.GetRequired).ToArray();

    public IReadOnlyList<BPackageDefinition> GetVersions(string packageId)
    {
        lock (_viewGate)
        {
            EnsurePackageViews();
            if (_versionViews.TryGetValue(packageId, out var versions)) return versions;
            var available = _catalog.GetVersions(packageId);
            var hasInstalled = _activeDefinitions.TryGetValue(packageId, out var installed);
            if (available.Count == 0 && !hasInstalled) return [];
            versions = Array.AsReadOnly(available.Concat(hasInstalled ? [installed!] : [])
                .DistinctBy(item => item.Document.PackageVersion)
                .OrderByDescending(item => item.Document.PackageVersion, BPackageVersion.Comparer).ToArray());
            _versionViews.Add(packageId, versions);
            return versions;
        }
    }

    public IReadOnlyList<BPackageDefinition> GetDependencies(BPackageDefinition definition)
    {
        if (ReferenceEquals(_previewResolution, _resolutionCatalog) && ReferenceEquals(_previewDefinition, definition))
            return _previewDependencies;
        var selected = new BPackageCatalog(_resolutionCatalog.packages
            .Where(item => !item.Document.Id.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Path).Append(definition.Path), validateGraph: false);
        var dependencies = selected.GetDependencyClosure(definition.Document.Id).Select(selected.GetRequired).ToArray();
        _previewResolution = _resolutionCatalog;
        _previewDefinition = definition;
        return _previewDependencies = dependencies;
    }

    public void InstallVersion(string packageId, string version) => ChangeEnabled(packageId, true, version);

    public void SetEnabled(string packageId, bool enabled) => ChangeEnabled(packageId, enabled, null);

    private void ChangeEnabled(string packageId, bool enabled, string? version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        EnsureEditModeForPackageMutation();
        if (IsRetiredPackage(packageId))
            throw new KeyNotFoundException($"Package '{packageId}' was retired from the extension catalog.");
        _resolutionCatalog = ResolveInstalledCatalog();
        if (!_resolutionCatalog.TryGet(packageId, out var selected))
            throw new KeyNotFoundException($"Package '{packageId}' was not found.");
        if (!enabled && IsRequired(packageId))
            throw new InvalidOperationException($"Required package '{packageId}' cannot be disabled.");
        if (!enabled && selected.Document.Type == "project")
            throw new InvalidOperationException("Project packages are editable project content. Remove their files explicitly from Assets/3rd.");

        var previous = Snapshot();
        try
        {
            var changed = false;
            if (version is not null)
            {
                if (!_catalog.TryGet(packageId, version, out var requested))
                    throw new InvalidOperationException($"Package '{packageId}' version '{version}' is unavailable for engine {EngineVersion.Current}.");
                var reference = Matches10577();
                [global::BEngine.SkipLoopGuard]
                global::BEngine.Editor.Documents.PackageReferenceDocument? Matches10577()
                {
                    var matchSource10577 = _manifest.Packages;
                    ArgumentNullException.ThrowIfNull(matchSource10577, "source");
                    if (matchSource10577.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument>))
                    {
                        foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.Documents.PackageReferenceDocument>)matchSource10577)
                        {
                            if (item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                                return item;
                        }
                
                        return default!;
                    }
                
                    global::System.ReadOnlySpan<global::BEngine.Editor.Documents.PackageReferenceDocument> matchValues10577 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource10577);
                    foreach (var item in matchValues10577)
                    {
                        if (item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                            return item;
                    }
                
                    return default!;
                }
                if (reference is not null && reference.Version != version && IsEnabled(packageId) && selected.Document.Type == "project" &&
                    EditorHostServices.FileSystem.FileExists(EditorHostServices.Paths.Combine(_workspace.ThirdPartyPath,
                        selected.Document.ProjectDirectory, "package.yaml")))
                    throw new InvalidOperationException("Imported project package sources belong to the project. Remove their files before importing another version.");
                if (reference is null)
                {
                    reference = new PackageReferenceDocument { Id = packageId, Enabled = false };
                    _manifest.Packages.Add(reference);
                    changed = true;
                }
                changed |= reference.Version != version;
                reference.Version = version;
                reference.Type = requested.Document.Type;
                reference.ProjectDirectory = requested.Document.ProjectDirectory;
                _resolutionCatalog = ResolveInstalledCatalog();
            }
            changed |= enabled ? EnableWithDependencies(packageId) : DisableWithDependents(packageId);
            changed |= EnableRequiredPackages();
            if (!changed) return;
            _resolutionCatalog.ValidateEnabled(_manifest.Packages.Where(item => item.Enabled).Select(item => item.Id));
            var disabling = _activeDefinitions.Values.Where(definition =>
                WasEnabled(previous, definition.Document.Id) && (!IsEnabled(definition.Document.Id) ||
                    _resolutionCatalog.GetRequired(definition.Document.Id).Document.PackageVersion != definition.Document.PackageVersion)).ToArray();
            EditorCallbackDispatcher.Invoke(packagesReloading, nameof(packagesReloading));
            if (disabling.Length > 0)
                EditorCallbackDispatcher.Invoke(packagesUnloading, disabling, nameof(packagesUnloading));
            Save();
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
        }
        catch
        {
            Restore(previous);
            _resolutionCatalog = ResolveInstalledCatalog();
            Save();
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
            throw;
        }
    }

    /// <summary>Refreshes available metadata without reloading active project assemblies.</summary>
    public void RefreshAvailablePackages()
    {
        if (_discoverRepository) _catalog = BPackageCatalog.Discover(_workspace);
    }

    public void Reload()
    {
        EnsureEditModeForPackageMutation();
        var previous = Snapshot();
        var previousCatalog = _resolutionCatalog;
        var previousSourceCatalog = _catalog;
        try
        {
            if (_discoverRepository) _catalog = BPackageCatalog.Discover(_workspace);
            _manifest = LoadOrCreate();
            _resolutionCatalog = ResolveInstalledCatalog();
            NormalizeManifest();
        }
        catch
        {
            Restore(previous);
            _catalog = previousSourceCatalog;
            _resolutionCatalog = previousCatalog;
            throw;
        }
        var disabling = previousCatalog.packages.Where(definition =>
            WasEnabled(previous, definition.Document.Id) && !IsEnabled(definition.Document.Id)).ToArray();
        try
        {
            EditorCallbackDispatcher.Invoke(packagesReloading, nameof(packagesReloading));
            if (disabling.Length > 0)
                EditorCallbackDispatcher.Invoke(packagesUnloading, disabling, nameof(packagesUnloading));
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
        }
        catch
        {
            Restore(previous);
            _catalog = previousSourceCatalog;
            _resolutionCatalog = previousCatalog;
            Save();
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
            throw;
        }
    }

    private static void EnsureEditModeForPackageMutation()
    {
        EditorToolingLifecycle.PreparePackageMutation();
    }

    public void ExportEnabledRuntimePackages(string outputDirectory, Action<int, int, string>? progress = null)
    {
        var outputPath = EditorHostServices.Paths.GetFullPath(outputDirectory);
        using var buildSession = EditorBuildEvents.BeginBuild(outputPath);
        var succeeded = false;
        try
        {
            PackageBuildExporter.ExportRuntimePackages(
                _workspace,
                outputPath,
                _manifest.Packages.Where(package => package.Enabled && package.Type != "project").ToArray(),
                _activeDefinitions.Values.Where(definition => definition.Document.Type == "package").ToArray(),
                progress);
            succeeded = true;
        }
        finally { buildSession.Complete(succeeded); }
    }

    public void Dispose() => _assemblyManager?.Dispose();

    private bool EnableWithDependencies(string packageId)
    {
        var changed = false;
        foreach (var dependency in _resolutionCatalog.GetDependencyClosure(packageId))
            changed |= SetPackageValue(dependency, true);
        return SetPackageValue(packageId, true) | changed;
    }

    private bool DisableWithDependents(string packageId)
    {
        var dependents = _resolutionCatalog.GetDependentClosure(packageId);
        var requiredDependent = Matches17661();
        [global::BEngine.SkipLoopGuard]
        string? Matches17661()
        {
            var matchSource17661 = dependents;
            ArgumentNullException.ThrowIfNull(matchSource17661, "source");
            global::System.ReadOnlySpan<string> matchValues17661;
            if (matchSource17661.GetType() == typeof(string[]))
                matchValues17661 = (string[])(object)matchSource17661;
            else if (matchSource17661.GetType() == typeof(global::System.Collections.Generic.List<string>))
                matchValues17661 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)matchSource17661);
            else
            {
                foreach (var dependent in (global::System.Collections.Generic.IEnumerable<string>)matchSource17661)
                {
                    if (IsEnabled(dependent) && (IsRequired(dependent) || _resolutionCatalog.GetRequired(dependent).Document.Type == "project"))
                        return dependent;
                }
        
                return default!;
            }
        
            foreach (var dependent in matchValues17661)
            {
                if (IsEnabled(dependent) && (IsRequired(dependent) || _resolutionCatalog.GetRequired(dependent).Document.Type == "project"))
                    return dependent;
            }
        
            return default!;
        }
        if (requiredDependent is not null)
            throw new InvalidOperationException(
                $"Package '{packageId}' is required by '{requiredDependent}' and cannot be disabled.");
        var changed = false;
        foreach (var dependent in dependents) changed |= SetPackageValue(dependent, false);
        return SetPackageValue(packageId, false) | changed;
    }

    private bool SetPackageValue(string packageId, bool enabled)
    {
        if (IsRetiredPackage(packageId)) return false;
        var package = Matches18402();
        [global::BEngine.SkipLoopGuard]
        global::BEngine.Editor.Documents.PackageReferenceDocument? Matches18402()
        {
            var matchSource18402 = _manifest.Packages;
            ArgumentNullException.ThrowIfNull(matchSource18402, "source");
            if (matchSource18402.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument>))
            {
                foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.Documents.PackageReferenceDocument>)matchSource18402)
                {
                    if (item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                        return item;
                }
        
                return default!;
            }
        
            global::System.ReadOnlySpan<global::BEngine.Editor.Documents.PackageReferenceDocument> matchValues18402 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource18402);
            foreach (var item in matchValues18402)
            {
                if (item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))
                    return item;
            }
        
            return default!;
        }
        if (package is null)
        {
            if (!enabled) return false;
            var definition = _resolutionCatalog.GetRequired(packageId).Document;
            _manifest.Packages.Add(new PackageReferenceDocument
            {
                Id = packageId,
                Version = definition.PackageVersion,
                Type = definition.Type,
                ProjectDirectory = definition.ProjectDirectory,
                Enabled = enabled
            });
            return true;
        }
        if (package.Enabled == enabled) return false;
        if (enabled && _resolutionCatalog.GetRequired(packageId).Document.PackageVersion != package.Version)
            throw new InvalidOperationException($"Pinned package '{packageId}' version '{package.Version}' is unavailable. Select an available version explicitly.");
        package.Enabled = enabled;
        return true;
    }

    private PackageManifestDocument LoadOrCreate()
    {
        if (EditorHostServices.FileSystem.FileExists(_workspace.PackageManifestPath))
        {
            var document = YamlUtility.Load<PackageManifestDocument>(_workspace.PackageManifestPath);
            if (document.Format == "BEngine.Packages" && document.Version == 1)
            {
                if (document.Packages.RemoveAll(package => BuiltInModulePackageIds.Contains(package.Id)) > 0)
                    document.Save(_workspace.PackageManifestPath);
                return document;
            }
            throw new InvalidDataException($"Unsupported package manifest '{document.Format}' v{document.Version}.");
        }
        var manifest = new PackageManifestDocument();
        manifest.Save(_workspace.PackageManifestPath);
        return manifest;
    }

    private void NormalizeManifest()
    {
        var replacementOwners = ReplacementOwners();
        var replacements = _manifest.Packages.Where(reference => replacementOwners.ContainsKey(reference.Id))
            .GroupBy(reference => replacementOwners[reference.Id], StringComparer.OrdinalIgnoreCase)
            .Select(group => (Id: group.Key, Enabled: group.Any(reference => reference.Enabled))).ToArray();
        foreach (var replacement in replacements)
        {
            var definition = _resolutionCatalog.GetRequired(replacement.Id);
            var existing = Matches20625();
            [global::BEngine.SkipLoopGuard]
            global::BEngine.Editor.Documents.PackageReferenceDocument? Matches20625()
            {
                var matchSource20625 = _manifest.Packages;
                ArgumentNullException.ThrowIfNull(matchSource20625, "source");
                if (matchSource20625.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument>))
                {
                    foreach (var package in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.Documents.PackageReferenceDocument>)matchSource20625)
                    {
                        if (package.Id.Equals(replacement.Id, StringComparison.OrdinalIgnoreCase))
                            return package;
                    }
            
                    return default!;
                }
            
                global::System.ReadOnlySpan<global::BEngine.Editor.Documents.PackageReferenceDocument> matchValues20625 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource20625);
                foreach (var package in matchValues20625)
                {
                    if (package.Id.Equals(replacement.Id, StringComparison.OrdinalIgnoreCase))
                        return package;
                }
            
                return default!;
            }
            if (replacement.Enabled && existing is not null && existing.Version != definition.Document.PackageVersion)
                throw new InvalidOperationException($"Pinned package '{existing.Id}' version '{existing.Version}' is unavailable. Select an available version explicitly.");
        }
        var changed = false;
        foreach (var replacement in replacements)
            if (!_manifest.Packages.Any(package => package.Id.Equals(replacement.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var document = _resolutionCatalog.GetRequired(replacement.Id).Document;
                _manifest.Packages.Add(new PackageReferenceDocument
                {
                    Id = document.Id, Version = document.PackageVersion, Type = document.Type,
                    ProjectDirectory = document.ProjectDirectory, Enabled = replacement.Enabled
                });
                changed = true;
            }
            else if (replacement.Enabled)
                changed |= SetPackageValue(replacement.Id, replacement.Enabled);
        changed |= _manifest.Packages.RemoveAll(package => replacementOwners.ContainsKey(package.Id)) > 0;
        foreach (var package in _manifest.Packages)
        {
            if (!_resolutionCatalog.TryGet(package.Id, out var definition)) continue;
            if (package.Type != definition.Document.Type) { package.Type = definition.Document.Type; changed = true; }
            if (package.Type == "project" && string.IsNullOrWhiteSpace(package.ProjectDirectory))
            {
                package.ProjectDirectory = definition.Document.ProjectDirectory;
                changed = true;
            }
        }
        changed |= EnableRequiredPackages();
        foreach (var package in _manifest.Packages.Where(package => package.Enabled).ToArray())
            foreach (var dependency in _resolutionCatalog.GetDependencyClosure(package.Id))
                changed |= SetPackageValue(dependency, true);
        _resolutionCatalog.ValidateEnabled(_manifest.Packages.Where(item => item.Enabled).Select(item => item.Id));
        if (changed) Save();
    }

    private bool EnableRequiredPackages()
    {
        var changed = false;
        foreach (var packageId in GetRequiredPackageIds()) changed |= SetPackageValue(packageId, true);
        return changed;
    }

    private void EnsureRequiredPackagePolicy()
    {
        if (ReferenceEquals(_requiredSourceCatalog, _catalog) &&
            ReferenceEquals(_requiredResolutionCatalog, _resolutionCatalog)) return;
        var requiredBy = new Dictionary<string, List<BPackageDefinition>>(StringComparer.OrdinalIgnoreCase);
        var roots = _catalog.GetRequiredPackageRoots().Concat(_resolutionCatalog.GetRequiredPackageRoots())
            .Select(root => root.Document.Id).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase);
        foreach (var rootId in roots)
        {
            var root = _resolutionCatalog.GetRequired(rootId);
            foreach (var packageId in _resolutionCatalog.GetDependencyClosure(rootId).Append(rootId))
            {
                _resolutionCatalog.GetRequired(packageId);
                if (!requiredBy.TryGetValue(packageId, out var owners)) requiredBy.Add(packageId, owners = []);
                owners.Add(root);
            }
        }
        _requiredBy = BuildIndex27716(requiredBy, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<global::BEngine.ProjectSystem.BPackageDefinition>> BuildIndex27716(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>>> indexSource27716, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer27716)
        {
            ArgumentNullException.ThrowIfNull(indexSource27716, "source");
            var knownCount = indexSource27716.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<global::BEngine.ProjectSystem.BPackageDefinition>>(indexComparer27716);
            global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>>> values;
            if (knownCount && indexSource27716 is global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>>[] array)
                values = array;
            else if (knownCount && indexSource27716 is global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>>> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<global::BEngine.ProjectSystem.BPackageDefinition>>(capacity, indexComparer27716);
                foreach (var item in indexSource27716)
                    result.Add(IndexKey27716(item), IndexValue27716(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.IReadOnlyList<global::BEngine.ProjectSystem.BPackageDefinition>>(values.Length, indexComparer27716);
                foreach (var item in values)
                    result.Add(IndexKey27716(item), IndexValue27716(item));
                return result;
            }
        
            string IndexKey27716(global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>> item) => item.Key;
            global::System.Collections.Generic.IReadOnlyList<global::BEngine.ProjectSystem.BPackageDefinition> IndexValue27716(global::System.Collections.Generic.KeyValuePair<string, global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition>> item) => (IReadOnlyList<BPackageDefinition>)item.Value.ToArray();
        }
        _requiredIds = Array.AsReadOnly(requiredBy.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray());
        _requiredSourceCatalog = _catalog;
        _requiredResolutionCatalog = _resolutionCatalog;
    }

    private BPackageCatalog ResolveInstalledCatalog()
    {
        if (_manifest.Packages is null)
            throw new InvalidDataException("Package manifest packages must be a list.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = BuildIndex28379(_catalog.packages.Where(definition => !IsRetiredPackage(definition.Document.Id) && EditorHostServices.FileSystem.FileExists(definition.Path)), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex28379(global::System.Collections.Generic.IEnumerable<global::BEngine.ProjectSystem.BPackageDefinition> indexSource28379, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer28379)
        {
            ArgumentNullException.ThrowIfNull(indexSource28379, "source");
            var knownCount = indexSource28379.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer28379);
            global::System.ReadOnlySpan<global::BEngine.ProjectSystem.BPackageDefinition> values;
            if (knownCount && indexSource28379 is global::BEngine.ProjectSystem.BPackageDefinition[] array)
                values = array;
            else if (knownCount && indexSource28379 is global::System.Collections.Generic.List<global::BEngine.ProjectSystem.BPackageDefinition> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer28379);
                foreach (var item in indexSource28379)
                    result.Add(IndexKey28379(item), IndexValue28379(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer28379);
                foreach (var item in values)
                    result.Add(IndexKey28379(item), IndexValue28379(item));
                return result;
            }
        
            string IndexKey28379(global::BEngine.ProjectSystem.BPackageDefinition definition) => definition.Document.Id;
            string IndexValue28379(global::BEngine.ProjectSystem.BPackageDefinition definition) => definition.Path;
        }
        foreach (var reference in _manifest.Packages)
        {
            if (reference is null || string.IsNullOrWhiteSpace(reference.Id))
                throw new InvalidDataException("Package manifest contains an entry without an id.");
            if (!ids.Add(reference.Id))
                throw new InvalidDataException($"Package manifest contains duplicate id '{reference.Id}'.");
            if (IsRetiredPackage(reference.Id)) continue;
            var hasRelease = _catalog.TryGet(reference.Id, reference.Version, out var release);
            if (hasRelease) paths[reference.Id] = release.Path;
            var isProject = reference.Type == "project" ||
                            (_catalog.TryGet(reference.Id, out var available) && available.Document.Type == "project");
            string installedPath;
            if (isProject)
            {
                var folder = reference.ProjectDirectory;
                if (string.IsNullOrWhiteSpace(folder) && _catalog.TryGet(reference.Id, out var definition))
                    folder = definition.Document.ProjectDirectory;
                folder = PackageDefinitionLoader.Normalize(new PackageDefinitionDocument
                {
                    Id = reference.Id, Type = "project", ProjectDirectory = folder
                }, _workspace.PackageManifestPath).ProjectDirectory;
                installedPath = EditorHostServices.Paths.Combine(_workspace.ThirdPartyPath, folder, "package.yaml");
            }
            else
            {
                if (_catalog.TryGet(reference.Id, reference.Version, out var selected))
                {
                    paths[reference.Id] = selected.Path;
                    continue;
                }
                installedPath = _cache.GetInstalledDefinitionPath(reference.Id);
            }
            if (!EditorHostServices.FileSystem.FileExists(installedPath))
            {
                if (reference.Enabled && !hasRelease)
                    throw new InvalidDataException($"Package '{reference.Id}' version '{reference.Version}' is unavailable for engine {EngineVersion.Current}.");
                continue;
            }
            var installed = PackageDefinitionLoader.Load(installedPath);
            if (!reference.Enabled && installed.PackageVersion != reference.Version) continue;
            if (!installed.Id.Equals(reference.Id, StringComparison.OrdinalIgnoreCase) ||
                (installed.Type == "project") != isProject || installed.PackageVersion != reference.Version)
                throw new InvalidDataException($"Installed package '{installedPath}' does not match manifest entry '{reference.Id}'.");
            if (!BPackageCompatibility.SupportsEngine(installed, EngineVersion.Current))
                throw new InvalidDataException($"Package '{installed.Id}' version '{installed.PackageVersion}' is unavailable for engine {EngineVersion.Current}.");
            paths[reference.Id] = installedPath;
        }
        return new BPackageCatalog(paths.Values, validateGraph: false);
    }

    private PackageReferenceDocument[] Snapshot() => _manifest.Packages.Select(package =>
        new PackageReferenceDocument
        {
            Id = package.Id,
            Version = package.Version,
            Type = package.Type,
            ProjectDirectory = package.ProjectDirectory,
            Enabled = package.Enabled
        }).ToArray();

    private static bool WasEnabled(IEnumerable<PackageReferenceDocument> packages, string packageId) =>
        packages.Any(package => package.Enabled &&
                                package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    private void Restore(IEnumerable<PackageReferenceDocument> values)
    {
        _manifest.Packages = Gather37595(values);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument> Gather37595(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.Documents.PackageReferenceDocument> items37595)
        {
            ArgumentNullException.ThrowIfNull(items37595, "source");
            global::BEngine.Editor.Documents.PackageReferenceDocument Evaluate37595(global::BEngine.Editor.Documents.PackageReferenceDocument package) => new PackageReferenceDocument
            {
                Id = package.Id,
                Version = package.Version,
                Type = package.Type,
                ProjectDirectory = package.ProjectDirectory,
                Enabled = package.Enabled
            };
            global::System.ReadOnlySpan<global::BEngine.Editor.Documents.PackageReferenceDocument> values;
            if (items37595.GetType() == typeof(global::BEngine.Editor.Documents.PackageReferenceDocument[]))
                values = (global::BEngine.Editor.Documents.PackageReferenceDocument[])items37595;
            else if (items37595.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument>))
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument>)items37595);
            else
            {
                return global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(items37595, Evaluate37595));
            }

            var result = new global::System.Collections.Generic.List<global::BEngine.Editor.Documents.PackageReferenceDocument>(values.Length);
            global::System.Runtime.InteropServices.CollectionsMarshal.SetCount(result, values.Length);
            var destination = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(result);
            for (var index = 0; index < values.Length; index++)
                destination[index] = Evaluate37595(values[index]);
            return result;
        }
    }

    private void Save() => _manifest.Save(_workspace.PackageManifestPath);

    private void ApplyRuntimePackageState()
    {
        var retiredPackageIds = ReplacementOwners().Keys.ToArray();
        if (_assemblyManager is not null)
        {
            foreach (var packageId in retiredPackageIds)
                RuntimePackageState.SetEnabled(packageId, false);
            foreach (var definition in _catalog.packages)
                RuntimePackageState.SetEnabled(definition.Document.Id, IsEnabled(definition.Document.Id));
            foreach (var package in _manifest.Packages)
                RuntimePackageState.SetEnabled(package.Id, package.Enabled);
        }
        _cache.RemoveRetired(retiredPackageIds);
        var enabledIds = _manifest.Packages.Where(package => package.Enabled)
            .Select(package => package.Id).ToArray();
        using var projectImport = ProjectPackageImporter.ImportEnabled(_workspace, _resolutionCatalog, _manifest.Packages);
        var projectDefinitions = projectImport.Definitions;
        _cache.Synchronize(_resolutionCatalog, enabledIds.Where(id => !projectDefinitions.ContainsKey(id)), definitions =>
        {
            _activeDefinitions = BuildIndex33903(definitions.Concat(projectDefinitions), StringComparer.OrdinalIgnoreCase);
            [global::BEngine.SkipLoopGuard]
            global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition> BuildIndex33903(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.BPackageDefinition>> indexSource33903, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer33903)
            {
                ArgumentNullException.ThrowIfNull(indexSource33903, "source");
                var knownCount = indexSource33903.TryGetNonEnumeratedCount(out var capacity);
                if (knownCount && capacity == 0)
                    return new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(indexComparer33903);
                global::System.ReadOnlySpan<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.BPackageDefinition>> values;
                if (knownCount && indexSource33903 is global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.BPackageDefinition>[] array)
                    values = array;
                else if (knownCount && indexSource33903 is global::System.Collections.Generic.List<global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.BPackageDefinition>> list)
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
                else
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(capacity, indexComparer33903);
                    foreach (var item in indexSource33903)
                        result.Add(IndexKey33903(item), IndexValue33903(item));
                    return result;
                }
            
                {
                    var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.ProjectSystem.BPackageDefinition>(values.Length, indexComparer33903);
                    foreach (var item in values)
                        result.Add(IndexKey33903(item), IndexValue33903(item));
                    return result;
                }
            
                string IndexKey33903(global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.BPackageDefinition> pair) => pair.Key;
                global::BEngine.ProjectSystem.BPackageDefinition IndexValue33903(global::System.Collections.Generic.KeyValuePair<string, global::BEngine.ProjectSystem.BPackageDefinition> pair) => pair.Value;
            }
            if (_assemblyManager is { } assemblyManager)
                assemblyManager.Apply(definitions.Values);
        });
        projectImport.Commit();
    }

    private IReadOnlyDictionary<string, string> ReplacementOwners()
    {
        lock (_viewGate)
        {
            if (ReferenceEquals(_replacementCatalog, _catalog) && MetadataMatches(_replacementMetadata)) return _replacementOwners;
            return RebuildReplacementOwners();
        }
    }

    private IReadOnlyDictionary<string, string> RebuildReplacementOwners()
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in _catalog.packages)
        foreach (var retired in definition.Document.Replaces)
        {
            if (owners.TryGetValue(retired, out var previous) &&
                !previous.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Package replacement '{retired}' has conflicting owners.");
            owners[retired] = definition.Document.Id;
        }
        _replacementOwners = owners;
        _replacementMetadata = _catalog.packages.Select(PackageViewMetadata.Capture).ToArray();
        _replacementCatalog = _catalog;
        return owners;
    }

    private bool IsRetiredPackage(string packageId) => ReplacementOwners().ContainsKey(packageId);

    private static bool MetadataMatches(PackageViewMetadata[] metadata)
    {
        foreach (var item in metadata)
            if (!item.Matches()) return false;
        return true;
    }

    private readonly record struct PackageViewMetadata(BPackageDefinition Definition,
        string Id, string DisplayName, string Version, string[] Replaces)
    {
        internal static PackageViewMetadata Capture(BPackageDefinition definition) => new(definition,
            definition.Document.Id, definition.Document.DisplayName, definition.Document.PackageVersion,
            definition.Document.Replaces.ToArray());

        internal bool Matches()
        {
            var document = Definition.Document;
            if (document.Id != Id || document.DisplayName != DisplayName || document.PackageVersion != Version ||
                document.Replaces.Count != Replaces.Length) return false;
            for (var index = 0; index < Replaces.Length; index++)
                if (document.Replaces[index] != Replaces[index]) return false;
            return true;
        }
    }
}
