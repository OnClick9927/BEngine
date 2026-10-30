namespace BEngine.AssetBundles;

public sealed partial class AssetBundleManager : IAssetBundlePackageManager
{
    public IReadOnlyList<AssetBundlePackageDescriptor> GetPackages()
    {
        EnsureInitialized();
        ThrowIfDisposed();
        if (_active is not { } state) return [];
        return Array.AsReadOnly(AssetBundlePackageUtility.GetPackages(state.Catalog)
            .OrderBy(static package => package.Name, StringComparer.Ordinal)
            .Select(ClonePackage).ToArray());
    }

    public BValueTask<AssetBundlePackageUpdatePlan> CheckPackageForUpdatesAsync(
        string packageName, BCancellationToken cancellationToken = default) =>
        CheckPackagesForUpdatesAsync([packageName], cancellationToken);

    public async BValueTask<AssetBundlePackageUpdatePlan> CheckPackagesForUpdatesAsync(
        IEnumerable<string> packageNames, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageNames);
        var requested = packageNames.ToArray();
        if (requested.Length == 0) throw new ArgumentException("Select at least one assetbundle package.", nameof(packageNames));
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            var current = _active;
            RuntimeState source;
            if (_options.RemoteBaseUri is null)
                source = current ?? throw new InvalidOperationException("There is no active catalog or remote source.");
            else
                source = await FetchPackageSourceAsync(cancellationToken);
            var selected = SelectPackageClosure(source.Catalog, requested);
            var packageSet = selected.Select(static package => package.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var currentPackages = GetCurrentPackages(current?.Catalog, source.Catalog);
            var changed = current is null || (current.Catalog.Packages.Count == 0 && source.Catalog.Packages.Count > 0) || selected.Any(package =>
                !currentPackages.TryGetValue(package.Name, out var installed) ||
                !installed.Version.Equals(package.Version, StringComparison.OrdinalIgnoreCase));
            // An unrelated release version never advances this client's package state.
            var target = changed ? MergePackageState(current, source, packageSet, currentPackages) : current!;
            var selectedBundles = ResolvePackageBundles(target.Catalog, packageSet);
            var downloads = new List<AssetBundleDescriptor>();
            foreach (var descriptor in selectedBundles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await HasValidBundleAsync(descriptor, cancellationToken)) downloads.Add(descriptor);
            }
            ThrowIfDisposed();
            return new AssetBundlePackageUpdatePlan(source.Version, target.Version, target.CatalogBytes,
                downloads, selected.Select(static package => package.Name).ToArray(),
                selectedBundles.Select(static bundle => bundle.Name).ToArray(),
                changed || downloads.Count > 0, AssetBundleResourceRevision.FromVersion(current?.Version));
        }
    }

    public async BValueTask<long> GetPackageDownloadSizeAsync(
        string packageName, BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            var state = _active ?? throw new InvalidOperationException("There is no active catalog.");
            var packages = SelectPackageClosure(state.Catalog, [packageName]);
            long size = 0;
            foreach (var descriptor in ResolvePackageBundles(state.Catalog, packages.Select(static package => package.Name)))
                if (!await HasValidBundleAsync(descriptor, cancellationToken)) size = checked(size + descriptor.Size);
            return size;
        }
    }

    public async BValueTask<AssetBundleUpdateResult> RepairPackageAsync(
        string packageName, IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            var state = _active ?? throw new InvalidOperationException("There is no active catalog.");
            var packages = SelectPackageClosure(state.Catalog, [packageName]);
            var selected = ResolvePackageBundles(state.Catalog, packages.Select(static package => package.Name));
            return await MaintainPackageObjectsAsync(state, selected, progress, cancellationToken);
        }
    }

    public async BValueTask<AssetBundleUpdateResult> ApplyPackageUpdateAsync(
        AssetBundlePackageUpdatePlan plan, IProgress<AssetBundleUpdateProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfDisposed();
        using (await EnterStateGateAsync(cancellationToken))
        {
            EnsureInitialized();
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (!_options.UsePersistentCache)
                throw new InvalidOperationException("AssetBundle package updates require a persistent cache.");
            if (AssetBundleResourceRevision.FromVersion(_active?.Version) != plan.BaseRevision)
                throw new InvalidOperationException("The active catalog changed after this package update was checked. Check the package again.");
            var version = plan.TargetVersion;
            VerifyPayload(plan.CatalogBytes.Span, version.CatalogSize, version.CatalogSha256, "package update catalog");
            var catalog = DeserializeCatalogPair(version, plan.CatalogBytes.Span, out var indexSeeds);
            var selected = ResolvePackageBundles(catalog, plan.PackageNames);
            var current = _active;
            var target = new RuntimeState(version, catalog, plan.CatalogBytes.ToArray(), indexSeeds);
            var result = await MaintainPackageObjectsAsync(target, selected, progress, cancellationToken,
                reportCompleted: false);
            if (SameRelease(current, target))
            {
                progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Completed,
                    selected.Count, selected.Count, 0, 0));
                ThrowIfDisposed();
                return result;
            }
            progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Activating,
                selected.Count, selected.Count, result.DownloadedBytes, result.DownloadedBytes));
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await PersistCatalogAsync(target, cancellationToken);
            if (current is not null)
            {
                await PersistCatalogAsync(current, cancellationToken);
                await WritePointerAsync(PreviousPointerPath, current.Version, cancellationToken);
            }
            else if (_fileSystem.FileExists(PreviousPointerPath))
                _fileSystem.DeleteFile(PreviousPointerPath);
            await WritePointerAsync(PendingPointerPath, version, cancellationToken);
            // Catalog and all selected payloads are durable before the atomic active-pointer swap.
            await WritePointerAsync(ActivePointerPath, version, cancellationToken);
            ThrowIfDisposed();
            _active = target;
            _hasPendingActivation = true;
            BAssetReferenceLoader.Clear();
            progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Completed,
                selected.Count, selected.Count, result.DownloadedBytes, result.DownloadedBytes));
            ThrowIfDisposed();
            return result with { Updated = true, PreviousVersion = current?.Version.Version ?? string.Empty,
                ActiveVersion = version.Version };
        }
    }

    private async BValueTask<AssetBundleUpdateResult> MaintainPackageObjectsAsync(
        RuntimeState state, IReadOnlyList<AssetBundleDescriptor> selected,
        IProgress<AssetBundleUpdateProgress>? progress, BCancellationToken cancellationToken,
        bool reportCompleted = true)
    {
        // Existing valid handles can keep their old immutable objects. A broken object being
        // repaired must first release its open handles, just as a local release installation does.
        _ = UnloadUnused();
        _localInstallInProgress = true;
        try
        {
            _localInstallUsesAsyncIo = !RuntimeServiceAccess.PlatformService.IsBrowser;
            _localInstallSliceStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var downloads = new List<AssetBundleDescriptor>();
            foreach (var descriptor in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await EnsurePackageObjectAsync(descriptor, cancellationToken)) downloads.Add(descriptor);
            }
            if (downloads.Count > 0 && (!_options.UsePersistentCache || _options.RemoteBaseUri is null))
                throw new InvalidOperationException("Repairing missing AssetBundle packages requires a remote source and persistent cache.");
            foreach (var descriptor in downloads)
                if (_loadedBundles.ContainsKey(descriptor.Sha256))
                    throw new InvalidOperationException($"Unload bundle '{descriptor.Name}' before repairing its object.");
            var total = downloads.Aggregate(0L, static (sum, bundle) => checked(sum + bundle.Size));
            long bytes = 0;
            var count = 0;
            progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Downloading, 0, downloads.Count, 0, total));
            foreach (var descriptor in downloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (await DownloadBundleAsync(state.Version, descriptor, cancellationToken))
                {
                    count++;
                    bytes = checked(bytes + descriptor.Size);
                }
                progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Downloading,
                    count, downloads.Count, bytes, total, descriptor.Name));
            }
            progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Verifying,
                downloads.Count, downloads.Count, total, total));
            foreach (var descriptor in selected)
                if (!await HasValidBundleAsync(descriptor, cancellationToken))
                    throw new InvalidDataException($"Bundle '{descriptor.Name}' is unavailable after package maintenance.");
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (reportCompleted)
                progress?.Report(new AssetBundleUpdateProgress(AssetBundleUpdatePhase.Completed,
                    downloads.Count, downloads.Count, total, total));
            ThrowIfDisposed();
            return new AssetBundleUpdateResult(false, _active?.Version.Version ?? string.Empty,
                _active?.Version.Version ?? string.Empty, count, bytes);
        }
        catch
        {
            CleanupStagingFiles();
            throw;
        }
        finally { _localInstallInProgress = false; }
    }

    private async BValueTask<bool> EnsurePackageObjectAsync(
        AssetBundleDescriptor descriptor, BCancellationToken cancellationToken)
    {
        var destination = GetObjectPath(descriptor);
        if (!_options.UsePersistentCache || !_fileSystem.FileExists(destination))
            return await HasValidBundleAsync(descriptor, cancellationToken);
        try
        {
            await AssetBundleFileVerifier.VerifyAsync(_fileSystem, destination, descriptor.Size,
                descriptor.Sha256, _options.MaximumBundleSize, cancellationToken);
            return true;
        }
        catch (Exception error) when (IsRecoverableContentFailure(error)) { }
        // The loader prefers an existing cache file. A valid built-in fallback must not mask
        // corruption in that preferred file: repair it locally before reporting success.
        if (!_builtInBundlePaths.TryGetValue(descriptor.Sha256, out var source) || !_fileSystem.FileExists(source))
            return false;
        try
        {
            await AssetBundleFileVerifier.VerifyAsync(_fileSystem, source, descriptor.Size,
                descriptor.Sha256, _options.MaximumBundleSize, cancellationToken);
        }
        catch (Exception error) when (IsRecoverableContentFailure(error)) { return false; }
        if (_loadedBundles.ContainsKey(descriptor.Sha256))
            throw new InvalidOperationException($"Unload bundle '{descriptor.Name}' before repairing its cached object.");
        await CopyLocalObjectAsync(source, destination, descriptor, cancellationToken);
        return true;
    }

    private async BValueTask<RuntimeState> FetchPackageSourceAsync(BCancellationToken cancellationToken)
    {
        var version = await FetchRemoteVersionAsync(cancellationToken);
        ValidatePackage(version.PackageName);
        var bytes = await FetchBytesWithRetryAsync(BuildRemoteUri(version.Version, version.CatalogFile),
            AssetBundleHttpResourceKind.Catalog, _options.MaximumCatalogSize, cancellationToken);
        VerifyPayload(bytes, version.CatalogSize, version.CatalogSha256, "remote package catalog");
        var catalog = DeserializeCatalogPair(version, bytes, out var indexSeeds);
        return new RuntimeState(version, catalog, bytes, indexSeeds);
    }

    private RuntimeState MergePackageState(RuntimeState? current, RuntimeState source,
        ISet<string> selected, IReadOnlyDictionary<string, AssetBundlePackageDescriptor> currentPackages)
    {
        var remotePackages = AssetBundlePackageUtility.GetPackages(source.Catalog);
        var retained = currentPackages.Values.Where(package => !selected.Contains(package.Name)).ToArray();
        var replaced = remotePackages.Where(package => selected.Contains(package.Name)).ToArray();
        var retainedBundles = retained.SelectMany(static package => package.Bundles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remoteBundles = replaced.SelectMany(static package => package.Bundles).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (retainedBundles.Overlaps(remoteBundles))
            throw new InvalidDataException("A bundle moved between packages. Update both owning packages together.");
        var catalog = new AssetBundleCatalog
        {
            PackageName = source.Catalog.PackageName,
            Version = source.Catalog.Version,
            IsPackageComposite = true,
            Bundles = (current?.Catalog.Bundles.Where(bundle => retainedBundles.Contains(bundle.Name))
                    .Select(bundle => ClonePackageBundle(bundle, current.Version.Version)) ?? [])
                .Concat(source.Catalog.Bundles.Where(bundle => remoteBundles.Contains(bundle.Name))
                    .Select(bundle => ClonePackageBundle(bundle, source.Version.Version))).ToList(),
            Assets = (current?.Catalog.Assets.Where(asset => retainedBundles.Contains(asset.Bundle)) ?? [])
                .Concat(source.Catalog.Assets.Where(asset => remoteBundles.Contains(asset.Bundle))).ToList(),
            Packages = retained.Concat(replaced).Select(ClonePackage).ToList()
        };
        // Validation also rejects address/GUID collisions and dependencies removed by the selected update.
        var bytes = AssetBundleCatalogSerializer.SerializeRuntimeCatalog(catalog);
        if (bytes.LongLength > _options.MaximumCatalogSize)
            throw new InvalidDataException("The merged package catalog exceeds the configured size limit.");
        ValidateCatalogLimits(catalog);
        var version = new AssetBundleVersion
        {
            PackageName = catalog.PackageName, Version = catalog.Version,
            CatalogFile = AssetBundleCatalogSerializer.RuntimeCatalogFileName, CatalogSize = bytes.LongLength,
            CatalogSha256 = AssetBundleCatalogSerializer.ComputeSha256(bytes)
        };
        return new RuntimeState(version, catalog, bytes);
    }

    private static Dictionary<string, AssetBundlePackageDescriptor> GetCurrentPackages(
        AssetBundleCatalog? current, AssetBundleCatalog target)
    {
        if (current is null) return new(StringComparer.OrdinalIgnoreCase);
        if (current.Packages.Count > 0 || target.Packages.Count == 0)
            return AssetBundlePackageUtility.GetPackages(current).ToDictionary(
                static package => package.Name, ClonePackage, StringComparer.OrdinalIgnoreCase);
        // Older catalogs have no package membership. Adopt the new ownership map for existing
        // bundle names without changing their payloads, so the first scoped update can migrate them.
        var owners = target.Packages.SelectMany(package => package.Bundles.Select(bundle => (bundle, package.Name)))
            .ToDictionary(static item => item.bundle, static item => item.Name, StringComparer.OrdinalIgnoreCase);
        return current.Bundles.GroupBy(bundle => owners.GetValueOrDefault(bundle.Name) ?? AssetBundlePackageDescriptor.DefaultName,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, group => new AssetBundlePackageDescriptor
            {
                Name = group.Key,
                Bundles = group.Select(static bundle => bundle.Name).ToList(),
                Version = AssetBundlePackageDescriptor.ComputeVersion(current, group.Select(static bundle => bundle.Name))
            }, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AssetBundlePackageDescriptor> SelectPackageClosure(
        AssetBundleCatalog catalog, IEnumerable<string> packageNames)
    {
        var packages = AssetBundlePackageUtility.GetPackages(catalog).ToDictionary(
            static package => package.Name, StringComparer.OrdinalIgnoreCase);
        var owners = packages.Values.SelectMany(package => package.Bundles.Select(bundle => (bundle, package.Name)))
            .ToDictionary(static item => item.bundle, static item => item.Name, StringComparer.OrdinalIgnoreCase);
        var bundles = catalog.Bundles.ToDictionary(static bundle => bundle.Name, StringComparer.OrdinalIgnoreCase);
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(packageNames);
        while (pending.TryDequeue(out var name))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            if (!packages.TryGetValue(name, out var package))
                throw new KeyNotFoundException($"Assetbundle package '{name}' was not found.");
            if (!selected.Add(package.Name)) continue;
            foreach (var bundle in package.Bundles)
            foreach (var dependency in bundles[bundle].Dependencies)
                if (!selected.Contains(owners[dependency])) pending.Enqueue(owners[dependency]);
        }
        return selected.OrderBy(static name => name, StringComparer.Ordinal).Select(name => packages[name]).ToArray();
    }

    private static IReadOnlyList<AssetBundleDescriptor> ResolvePackageBundles(
        AssetBundleCatalog catalog, IEnumerable<string> packageNames)
    {
        var selected = SelectPackageClosure(catalog, packageNames);
        return ResolveBundleSelection(catalog, selected.SelectMany(static package => package.Bundles));
    }

    private static AssetBundlePackageDescriptor ClonePackage(AssetBundlePackageDescriptor source) => new()
    {
        Name = source.Name, Version = source.Version, Bundles = source.Bundles.ToList()
    };

    private static AssetBundleDescriptor ClonePackageBundle(AssetBundleDescriptor source, string fallbackVersion) => new()
    {
        Name = source.Name, FileName = source.FileName, Sha256 = source.Sha256, Size = source.Size,
        Dependencies = source.Dependencies.ToList(),
        SourceVersion = string.IsNullOrEmpty(source.SourceVersion) ? fallbackVersion : source.SourceVersion
    };
}
