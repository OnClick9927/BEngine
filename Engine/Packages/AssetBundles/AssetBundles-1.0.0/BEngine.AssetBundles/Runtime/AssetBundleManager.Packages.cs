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
            var changed = current is null || (current.Catalog.Packages.Count == 0 && source.Catalog.Packages.Count > 0) || Query2120();
            bool Query2120()
            {
                var querySource2120 = selected;
                ArgumentNullException.ThrowIfNull(querySource2120, "source");
                if (querySource2120.GetType() == typeof(global::BEngine.AssetBundles.AssetBundlePackageDescriptor[]) || querySource2120.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundlePackageDescriptor>))
                {
                    global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundlePackageDescriptor> queryValues2120 = querySource2120.GetType() == typeof(global::BEngine.AssetBundles.AssetBundlePackageDescriptor[]) ? (global::BEngine.AssetBundles.AssetBundlePackageDescriptor[])(object)querySource2120 : global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundlePackageDescriptor>)(object)querySource2120);
                    foreach (var package in queryValues2120)
                    {
                        if (!currentPackages.TryGetValue(package.Name, out var installed) || !installed.Version.Equals(package.Version, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                else
                {
                    foreach (var package in querySource2120)
                    {
                        if (!currentPackages.TryGetValue(package.Name, out var installed) || !installed.Version.Equals(package.Version, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            
                return false;
            }
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
            return new AssetBundlePackageUpdatePlan(source.Version, target.Version, target.CatalogBytes, downloads, Gather4615(selected), Gather4690(selectedBundles), changed || downloads.Count > 0, AssetBundleResourceRevision.FromVersion(current?.Version));
            [global::BEngine.SkipLoopGuard]
            string[] Gather4690(global::System.Collections.Generic.IEnumerable<global::BEngine.AssetBundles.AssetBundleDescriptor> items4690)
            {
                ArgumentNullException.ThrowIfNull(items4690, "source");
                string Evaluate4690(global::BEngine.AssetBundles.AssetBundleDescriptor bundle) => bundle.Name;
                global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundleDescriptor> values;
                if (items4690.GetType() == typeof(global::BEngine.AssetBundles.AssetBundleDescriptor[]))
                    values = (global::BEngine.AssetBundles.AssetBundleDescriptor[])items4690;
                else if (items4690.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleDescriptor>))
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleDescriptor>)items4690);
                else
                {
                    return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items4690, Evaluate4690));
                }

                if (values.IsEmpty)
                    return global::System.Array.Empty<string>();
                var result = new string[values.Length];
                for (var index = 0; index < values.Length; index++)
                    result[index] = Evaluate4690(values[index]);
                return result;
            }

            [global::BEngine.SkipLoopGuard]
            string[] Gather4615(global::System.Collections.Generic.IEnumerable<global::BEngine.AssetBundles.AssetBundlePackageDescriptor> items4615)
            {
                ArgumentNullException.ThrowIfNull(items4615, "source");
                string Evaluate4615(global::BEngine.AssetBundles.AssetBundlePackageDescriptor package) => package.Name;
                global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundlePackageDescriptor> values;
                if (items4615.GetType() == typeof(global::BEngine.AssetBundles.AssetBundlePackageDescriptor[]))
                    values = (global::BEngine.AssetBundles.AssetBundlePackageDescriptor[])items4615;
                else if (items4615.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundlePackageDescriptor>))
                    values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundlePackageDescriptor>)items4615);
                else
                {
                    return global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(items4615, Evaluate4615));
                }

                if (values.IsEmpty)
                    return global::System.Array.Empty<string>();
                var result = new string[values.Length];
                for (var index = 0; index < values.Length; index++)
                    result[index] = Evaluate4615(values[index]);
                return result;
            }
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
            var target = CreateRuntimeState(version, catalog, plan.CatalogBytes.ToArray(), indexSeeds);
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
            BAsset.ClearLoadedAssets();
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
            _localInstallUsesAsyncIo = !RuntimeServices.GetRequiredService<global::BEngine.Platform.PlatformUtility>().IsBrowser;
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
        return CreateRuntimeState(version, catalog, bytes, indexSeeds);
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
        return CreateRuntimeState(version, catalog, bytes);
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
        var owners = BuildIndex18898(target.Packages.SelectMany(package => package.Bundles.Select(bundle => (bundle, package.Name))), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex18898(global::System.Collections.Generic.IEnumerable<(string bundle, string Name)> indexSource18898, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer18898)
        {
            ArgumentNullException.ThrowIfNull(indexSource18898, "source");
            var knownCount = indexSource18898.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer18898);
            global::System.ReadOnlySpan<(string bundle, string Name)> values;
            if (knownCount && indexSource18898 is (string bundle, string Name)[] array)
                values = array;
            else if (knownCount && indexSource18898 is global::System.Collections.Generic.List<(string bundle, string Name)> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer18898);
                foreach (var item in indexSource18898)
                    result.Add(IndexKey18898(item), IndexValue18898(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer18898);
                foreach (var item in values)
                    result.Add(IndexKey18898(item), IndexValue18898(item));
                return result;
            }
        
            string IndexKey18898((string bundle, string Name) item) => item.bundle;
            string IndexValue18898((string bundle, string Name) item) => item.Name;
        }
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
        var packages = BuildIndex19877(AssetBundlePackageUtility.GetPackages(catalog), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundlePackageDescriptor> BuildIndex19877(global::System.Collections.Generic.IEnumerable<global::BEngine.AssetBundles.AssetBundlePackageDescriptor> indexSource19877, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer19877)
        {
            ArgumentNullException.ThrowIfNull(indexSource19877, "source");
            var knownCount = indexSource19877.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundlePackageDescriptor>(indexComparer19877);
            global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundlePackageDescriptor> values;
            if (knownCount && indexSource19877 is global::BEngine.AssetBundles.AssetBundlePackageDescriptor[] array)
                values = array;
            else if (knownCount && indexSource19877 is global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundlePackageDescriptor> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundlePackageDescriptor>(capacity, indexComparer19877);
                foreach (var item in indexSource19877)
                    result.Add(IndexKey19877(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundlePackageDescriptor>(values.Length, indexComparer19877);
                foreach (var item in values)
                    result.Add(IndexKey19877(item), item);
                return result;
            }
        
            string IndexKey19877(global::BEngine.AssetBundles.AssetBundlePackageDescriptor package) => package.Name;
        }
        var owners = BuildIndex20038(packages.Values.SelectMany(package => package.Bundles.Select(bundle => (bundle, package.Name))), StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, string> BuildIndex20038(global::System.Collections.Generic.IEnumerable<(string bundle, string Name)> indexSource20038, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer20038)
        {
            ArgumentNullException.ThrowIfNull(indexSource20038, "source");
            var knownCount = indexSource20038.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, string>(indexComparer20038);
            global::System.ReadOnlySpan<(string bundle, string Name)> values;
            if (knownCount && indexSource20038 is (string bundle, string Name)[] array)
                values = array;
            else if (knownCount && indexSource20038 is global::System.Collections.Generic.List<(string bundle, string Name)> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(capacity, indexComparer20038);
                foreach (var item in indexSource20038)
                    result.Add(IndexKey20038(item), IndexValue20038(item));
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, string>(values.Length, indexComparer20038);
                foreach (var item in values)
                    result.Add(IndexKey20038(item), IndexValue20038(item));
                return result;
            }
        
            string IndexKey20038((string bundle, string Name) item) => item.bundle;
            string IndexValue20038((string bundle, string Name) item) => item.Name;
        }
        var bundles = BuildIndex20271(catalog.Bundles, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleDescriptor> BuildIndex20271(global::System.Collections.Generic.IEnumerable<global::BEngine.AssetBundles.AssetBundleDescriptor> indexSource20271, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer20271)
        {
            ArgumentNullException.ThrowIfNull(indexSource20271, "source");
            var knownCount = indexSource20271.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleDescriptor>(indexComparer20271);
            global::System.ReadOnlySpan<global::BEngine.AssetBundles.AssetBundleDescriptor> values;
            if (knownCount && indexSource20271 is global::BEngine.AssetBundles.AssetBundleDescriptor[] array)
                values = array;
            else if (knownCount && indexSource20271 is global::System.Collections.Generic.List<global::BEngine.AssetBundles.AssetBundleDescriptor> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleDescriptor>(capacity, indexComparer20271);
                foreach (var item in indexSource20271)
                    result.Add(IndexKey20271(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.AssetBundles.AssetBundleDescriptor>(values.Length, indexComparer20271);
                foreach (var item in values)
                    result.Add(IndexKey20271(item), item);
                return result;
            }
        
            string IndexKey20271(global::BEngine.AssetBundles.AssetBundleDescriptor bundle) => bundle.Name;
        }
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
