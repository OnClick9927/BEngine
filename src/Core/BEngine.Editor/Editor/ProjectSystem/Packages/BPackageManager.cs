using BEngine.Serialization;
using BEngine.Documents;
using BEngine.Editor.Documents;
using BEngine.Editor;

namespace BEngine.ProjectSystem;

public sealed class BPackageManager : IDisposable
{
    private static readonly string[] RetiredPackageIds =
    [
        "com.bengine.codex",
        "com.bengine.ugui"
    ];

    private readonly ProjectWorkspace _workspace;
    private readonly BPackageCatalog _catalog;
    private readonly ProjectPackageCache _cache;
    private readonly PackageAssemblyManager? _assemblyManager;
    private IReadOnlyDictionary<string, BPackageDefinition> _activeDefinitions =
        new Dictionary<string, BPackageDefinition>(StringComparer.OrdinalIgnoreCase);
    private PackageManifestDocument _manifest;

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
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _catalog = catalog ?? BPackageCatalog.Discover(workspace);
        _cache = new ProjectPackageCache(workspace);
        _manifest = LoadOrCreate();
        NormalizeManifest();
        _assemblyManager = loadAssemblies ? new PackageAssemblyManager(workspace, _catalog) : null;
        ApplyRuntimePackageState();
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
        _catalog.GetDependencyClosure(packageId).Select(_catalog.GetRequired).ToArray();

    public IReadOnlyList<BPackageDefinition> GetDependents(string packageId) =>
        _catalog.GetDependentClosure(packageId).Select(_catalog.GetRequired).ToArray();

    public void SetEnabled(string packageId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        if (IsRetiredPackage(packageId))
            throw new KeyNotFoundException($"Package '{packageId}' was retired from the extension catalog.");
        if (!_catalog.TryGet(packageId, out var selected))
            throw new KeyNotFoundException($"Package '{packageId}' was not found.");
        if (!enabled && selected.Document.Required)
            throw new InvalidOperationException($"Required package '{packageId}' cannot be disabled.");

        var previous = Snapshot();
        var changed = enabled ? EnableWithDependencies(packageId) : DisableWithDependents(packageId);
        if (!changed) return;

        var disabling = _catalog.packages.Where(definition =>
            WasEnabled(previous, definition.Document.Id) && !IsEnabled(definition.Document.Id)).ToArray();
        try
        {
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
            Save();
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
            throw;
        }
    }

    public void Reload()
    {
        var previous = Snapshot();
        _manifest = LoadOrCreate();
        NormalizeManifest();
        var disabling = _catalog.packages.Where(definition =>
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
            Save();
            ApplyRuntimePackageState();
            EditorCallbackDispatcher.Invoke(packagesChanged, nameof(packagesChanged));
            throw;
        }
    }

    public void ExportEnabledRuntimePackages(string outputDirectory, Action<int, int, string>? progress = null)
    {
        var outputPath = Path.GetFullPath(outputDirectory);
        BuildPipeline.RaiseBuildStarted(outputPath);
        var succeeded = false;
        try
        {
            PackageBuildExporter.ExportRuntimePackages(
                _workspace,
                outputPath,
                _manifest.Packages.Where(package => package.Enabled).ToArray(),
                _activeDefinitions.Values.ToArray(),
                progress);
            succeeded = true;
        }
        finally { BuildPipeline.RaiseBuildFinished(outputPath, succeeded); }
    }

    public void Dispose() => _assemblyManager?.Dispose();

    private bool EnableWithDependencies(string packageId)
    {
        var changed = false;
        foreach (var dependency in _catalog.GetDependencyClosure(packageId))
            changed |= SetPackageValue(dependency, true);
        return SetPackageValue(packageId, true) | changed;
    }

    private bool DisableWithDependents(string packageId)
    {
        var dependents = _catalog.GetDependentClosure(packageId);
        var requiredDependent = dependents.FirstOrDefault(dependent =>
            IsEnabled(dependent) && _catalog.GetRequired(dependent).Document.Required);
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
            var definition = _catalog.GetRequired(packageId).Document;
            _manifest.Packages.Add(new PackageReferenceDocument
            {
                Id = packageId,
                Version = definition.PackageVersion,
                Enabled = enabled
            });
            return true;
        }
        if (package.Enabled == enabled) return false;
        package.Enabled = enabled;
        return true;
    }

    private PackageManifestDocument LoadOrCreate()
    {
        if (File.Exists(_workspace.PackageManifestPath))
        {
            var document = Document.Load<PackageManifestDocument>(_workspace.PackageManifestPath);
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
        foreach (var package in _manifest.Packages.Where(package => package.Enabled).ToArray())
            foreach (var dependency in _catalog.GetDependencyClosure(package.Id))
                changed |= SetPackageValue(dependency, true);
        if (changed) Save();
    }

    private PackageReferenceDocument[] Snapshot() => _manifest.Packages.Select(package =>
        new PackageReferenceDocument
        {
            Id = package.Id,
            Version = package.Version,
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
        _cache.Synchronize(_catalog, enabledIds, definitions =>
        {
            _activeDefinitions = definitions;
            if (_assemblyManager is { } assemblyManager)
                EditorFeatureGuard.Invoke("PackageAssemblyManager.Apply",
                    () => assemblyManager.Apply(_activeDefinitions.Values));
        });
    }

    private static bool IsRetiredPackage(string packageId) =>
        RetiredPackageIds.Contains(packageId, StringComparer.OrdinalIgnoreCase);
}
