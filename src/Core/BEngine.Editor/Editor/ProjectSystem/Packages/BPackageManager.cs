using BEngine.Serialization;
using BEngine.Editor.Documents;
using BEngine.Editor;
using BEngine.Platform;

namespace BEngine.ProjectSystem;

public sealed class BPackageManager : IDisposable
{
    private static readonly string[] RetiredPackageIds =
    [
        "com.bengine.media",
        "com.bengine.ugui"
    ];

    private readonly ProjectWorkspace _workspace;
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

    public event Action? packagesChanged;
    public event Action? packagesReloading;
    public event Action<IReadOnlyList<BPackageDefinition>>? packagesUnloading;
    public IReadOnlyList<PackageReferenceDocument> packages => _manifest.Packages;
    public IReadOnlyList<BPackageDefinition> definitions => _catalog.packages
        .Where(definition => !IsRetiredPackage(definition.Document.Id))
        .Select(definition => _activeDefinitions.GetValueOrDefault(definition.Document.Id, definition))
        .Concat(_activeDefinitions.Values.Where(definition =>
            !IsRetiredPackage(definition.Document.Id) && !_catalog.TryGet(definition.Document.Id, out _)))
        .OrderBy(definition => definition.Document.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public BPackageManager(ProjectWorkspace workspace) : this(workspace, null) { }

    public BPackageManager(ProjectWorkspace workspace, BPackageCatalog? catalog)
        : this(workspace, catalog, loadAssemblies: true)
    {
    }

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
        catch
        {
            _assemblyManager?.Dispose();
            throw;
        }
    }

    public bool IsEnabled(string packageId) => _manifest.Packages.Any(package =>
        package.Enabled && package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

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

    public IReadOnlyList<BPackageDefinition> GetVersions(string packageId) => _catalog.GetVersions(packageId)
        .Concat(_activeDefinitions.TryGetValue(packageId, out var installed) ? [installed] : [])
        .DistinctBy(item => item.Document.PackageVersion)
        .OrderByDescending(item => item.Document.PackageVersion, BPackageVersion.Comparer).ToArray();

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
        if (!enabled && selected.Document.Required)
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
                var reference = _manifest.Packages.FirstOrDefault(item => item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
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

    public void Reload()
    {
        EnsureEditModeForPackageMutation();
        var previous = Snapshot();
        var previousCatalog = _resolutionCatalog;
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
            _resolutionCatalog = previousCatalog;
            Save();
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
            throw;
        }
    }

    private static void EnsureEditModeForPackageMutation()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Packages cannot change during a Play Mode transition.");
    }

    public void ExportEnabledRuntimePackages(string outputDirectory, Action<int, int, string>? progress = null)
    {
        var outputPath = EditorHostServices.Paths.GetFullPath(outputDirectory);
        EditorBuildEvents.RaiseBuildStarted(outputPath);
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
        finally { EditorBuildEvents.RaiseBuildFinished(outputPath, succeeded); }
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
        var requiredDependent = dependents.FirstOrDefault(dependent =>
            IsEnabled(dependent) && (_resolutionCatalog.GetRequired(dependent).Document.Required ||
                                    _resolutionCatalog.GetRequired(dependent).Document.Type == "project"));
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
        var package = _manifest.Packages.FirstOrDefault(item =>
            item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
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
            if (document.Format == "BEngine.Packages" && document.Version == 1) return document;
            throw new InvalidDataException($"Unsupported package manifest '{document.Format}' v{document.Version}.");
        }
        var manifest = new PackageManifestDocument();
        manifest.Save(_workspace.PackageManifestPath);
        return manifest;
    }

    private void NormalizeManifest()
    {
        var changed = _manifest.Packages.RemoveAll(package => IsRetiredPackage(package.Id)) > 0;
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
        foreach (var definition in _resolutionCatalog.packages.Where(definition => definition.Document.Required))
            changed |= SetPackageValue(definition.Document.Id, true);
        foreach (var package in _manifest.Packages.Where(package => package.Enabled).ToArray())
            foreach (var dependency in _resolutionCatalog.GetDependencyClosure(package.Id))
                changed |= SetPackageValue(dependency, true);
        _resolutionCatalog.ValidateEnabled(_manifest.Packages.Where(item => item.Enabled).Select(item => item.Id));
        if (changed) Save();
    }

    private BPackageCatalog ResolveInstalledCatalog()
    {
        if (_manifest.Packages is null)
            throw new InvalidDataException("Package manifest packages must be a list.");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = _catalog.packages
            .Where(definition => EditorHostServices.FileSystem.FileExists(definition.Path))
            .ToDictionary(definition => definition.Document.Id,
                definition => definition.Path, StringComparer.OrdinalIgnoreCase);
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
        _manifest.Packages = values.Select(package => new PackageReferenceDocument
        {
            Id = package.Id,
            Version = package.Version,
            Type = package.Type,
            ProjectDirectory = package.ProjectDirectory,
            Enabled = package.Enabled
        }).ToList();
    }

    private void Save() => _manifest.Save(_workspace.PackageManifestPath);

    private void ApplyRuntimePackageState()
    {
        foreach (var packageId in RetiredPackageIds)
            RuntimePackageState.SetEnabled(packageId, false);
        _cache.RemoveRetired(RetiredPackageIds);
        foreach (var definition in _catalog.packages)
            RuntimePackageState.SetEnabled(definition.Document.Id, IsEnabled(definition.Document.Id));
        foreach (var package in _manifest.Packages)
            RuntimePackageState.SetEnabled(package.Id, package.Enabled);
        var enabledIds = _manifest.Packages.Where(package => package.Enabled)
            .Select(package => package.Id).ToArray();
        using var projectImport = ProjectPackageImporter.ImportEnabled(_workspace, _resolutionCatalog, _manifest.Packages);
        var projectDefinitions = projectImport.Definitions;
        _cache.Synchronize(_resolutionCatalog, enabledIds.Where(id => !projectDefinitions.ContainsKey(id)), definitions =>
        {
            _activeDefinitions = definitions.Concat(projectDefinitions)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            if (_assemblyManager is { } assemblyManager)
                assemblyManager.Apply(definitions.Values);
        });
        projectImport.Commit();
    }

    private static bool IsRetiredPackage(string packageId) =>
        RetiredPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);
}
