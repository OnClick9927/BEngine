using BEngine.Serialization;
using BEngine.Serialization.Documents;

namespace BEngine.ProjectSystem;

public sealed class BPackageManager
{
    private readonly ProjectWorkspace _workspace;
    private readonly BPackageCatalog _catalog;
    private PackageManifestDocument _manifest;

    public event Action? packagesChanged;
    public IReadOnlyList<PackageReferenceDocument> packages => _manifest.Packages;
    public IReadOnlyList<BPackageDefinition> definitions => _catalog.packages;

    public BPackageManager(ProjectWorkspace workspace)
        : this(workspace, null)
    {
    }

    public BPackageManager(ProjectWorkspace workspace, BPackageCatalog? catalog)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _catalog = catalog ?? BPackageCatalog.Discover(workspace);
        _manifest = LoadOrCreate();
        EnsureBuiltInPackages();
        ApplyRuntimePackageState();
    }

    public bool IsEnabled(string packageId) => _manifest.Packages.Any(package =>
        package.Enabled && package.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

    public void SetEnabled(string packageId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var changed = false;
        if (enabled)
        {
            foreach (var dependency in _catalog.GetDependencyClosure(packageId))
                changed |= SetPackageValue(dependency, true);
        }
        else
        {
            if (_catalog.TryGet(packageId, out var definition) && definition.Document.Required)
                throw new InvalidOperationException($"Required package '{packageId}' cannot be disabled.");

            var dependents = _catalog.GetDependentClosure(packageId);
            var requiredDependent = dependents.FirstOrDefault(dependent =>
                IsEnabled(dependent) && _catalog.TryGet(dependent, out var candidate) &&
                candidate.Document.Required);
            if (requiredDependent is not null)
                throw new InvalidOperationException(
                    $"Package '{packageId}' is required by '{requiredDependent}' and cannot be disabled.");
            foreach (var dependent in dependents) changed |= SetPackageValue(dependent, false);
        }
        changed |= SetPackageValue(packageId, enabled);
        if (!changed) return;

        Save();
        ApplyRuntimePackageState();
        packagesChanged?.Invoke();
    }

    private bool SetPackageValue(string packageId, bool enabled)
    {
        var package = _manifest.Packages.FirstOrDefault(item =>
            item.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
        if (package is null)
        {
            package = new PackageReferenceDocument { Id = packageId, Enabled = enabled };
            _manifest.Packages.Add(package);
        }
        else if (package.Enabled == enabled)
        {
            return false;
        }
        else
        {
            package.Enabled = enabled;
        }

        return true;
    }

    private PackageManifestDocument LoadOrCreate()
    {
        if (File.Exists(_workspace.PackageManifestPath))
        {
            var document = YamlUtility.Load<PackageManifestDocument>(_workspace.PackageManifestPath);
            if (document.Format == "BEngine.Packages" && document.Version == 1) return document;
            throw new InvalidDataException($"Unsupported package manifest '{document.Format}' v{document.Version}.");
        }

        var manifest = new PackageManifestDocument
        {
            Packages = _catalog.packages.Select(package => new PackageReferenceDocument
            {
                Id = package.Document.Id,
                Version = package.Document.PackageVersion,
                Enabled = package.Document.Required || package.Document.EnabledByDefault
            }).ToList()
        };
        YamlUtility.Save(manifest, _workspace.PackageManifestPath);
        return manifest;
    }

    private void EnsureBuiltInPackages()
    {
        var changed = _manifest.Packages.RemoveAll(package =>
            package.Id.Equals("com.bengine.ugui", StringComparison.OrdinalIgnoreCase)) > 0;
        foreach (var definition in _catalog.packages)
        {
            var package = _manifest.Packages.FirstOrDefault(candidate =>
                candidate.Id.Equals(definition.Document.Id, StringComparison.OrdinalIgnoreCase));
            if (package is not null)
            {
                if (definition.Document.Required && !package.Enabled)
                {
                    package.Enabled = true;
                    changed = true;
                }
                continue;
            }
            _manifest.Packages.Add(new PackageReferenceDocument
            {
                Id = definition.Document.Id,
                Version = definition.Document.PackageVersion,
                Enabled = definition.Document.Required || definition.Document.EnabledByDefault
            });
            changed = true;
        }

        foreach (var package in _manifest.Packages.Where(package => package.Enabled).ToArray())
        {
            foreach (var dependency in _catalog.GetDependencyClosure(package.Id))
                changed |= SetPackageValue(dependency, true);
        }
        if (changed) Save();
    }

    private void Save() => YamlUtility.Save(_manifest, _workspace.PackageManifestPath);

    private void ApplyRuntimePackageState()
    {
        foreach (var package in _manifest.Packages)
            BEngine.RuntimePackageState.SetEnabled(package.Id, package.Enabled);
    }
}
