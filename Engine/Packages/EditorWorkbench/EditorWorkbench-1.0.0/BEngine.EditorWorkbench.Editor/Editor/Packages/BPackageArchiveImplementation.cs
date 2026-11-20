using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using BEngine.Serialization;
using BEngine.Platform;
using BEngine.ProjectSystem;

namespace BEngine.Editor;

internal static class BPackageArchiveImplementation
{

    internal static BPackageManifest ExportPackage(
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options,
        Action<BPackageProgress>? progress,
        BPackageArchiveServices services)
    {
        var projectPath = EditorApplication.projectPath;
        if (string.IsNullOrEmpty(projectPath))
            throw new InvalidOperationException("No editor project is open.");
        return ExportPackage(
            ProjectWorkspace.Open(projectPath, services.FileSystem, services.PathService),
            paths, destination, options, progress, services);
    }


    internal static BPackageManifest ExportPackage(
        ProjectWorkspace workspace,
        IEnumerable<string> paths,
        string destination,
        BPackageExportOptions? options,
        Action<BPackageProgress>? progress,
        BPackageArchiveServices services)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        options ??= new BPackageExportOptions();

        var destinationPath = services.PathService.GetFullPath(destination);
        if (!services.PathService.GetExtension(destinationPath)
                .Equals(".bpackage", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("BEngine package archives must use the .bpackage extension.", nameof(destination));
        var packageName = string.IsNullOrWhiteSpace(options.Name)
            ? services.PathService.GetFileNameWithoutExtension(destinationPath)
            : options.Name.Trim();
        if (packageName.Length == 0 || string.IsNullOrWhiteSpace(options.PackageVersion))
            throw new ArgumentException("Package name and version cannot be empty.", nameof(options));

        var sources = CollectSources(
            workspace, paths, options.IncludeMetaFiles, destinationPath, services);
        if (sources.Count == 0) throw new InvalidOperationException("The package selection contains no assets.");
        var entries = new List<BPackageManifestEntry>(sources.Count);
        var inspected = 0;
        foreach (var (relativePath, source) in sources)
        {
            var entry = source.IsDirectory
                ? new BPackageManifestEntry { RelativePath = relativePath, IsDirectory = true }
                : CreateFileManifestEntry(relativePath, source.FullPath, services.FileSystem);
            entries.Add(entry);
            Report(progress, BPackageProgressPhase.Inspecting, ++inspected, sources.Count, relativePath);
        }

        var manifest = new BPackageManifest
        {
            Format = BPackageArchiveFormat.Format,
            Version = BPackageArchiveFormat.Version,
            Name = packageName,
            PackageVersion = options.PackageVersion.Trim(),
            Description = options.Description?.Trim() ?? string.Empty,
            DefaultImportPath = string.IsNullOrWhiteSpace(options.DefaultImportPath)
                ? string.Empty
                : BPackagePathUtility.NormalizeRelativePath(
                    options.DefaultImportPath.Trim(), services.PathService),
            Entries = entries
        };
        ValidateManifest(manifest, services.PathService);

        var directory = services.PathService.GetDirectoryName(destinationPath) ??
                        services.PlatformService.CurrentDirectory;
        services.FileSystem.CreateDirectory(directory);
        var temporaryPath = services.PathService.Combine(
            directory,
            $".{services.PathService.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = services.FileSystem.OpenFile(
                       temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                       bufferSize: 4096, FileOptions.SequentialScan))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8))
            {
                WriteManifest(archive, manifest);
                var exported = 0;
                {
                    var __filteredSource4353 = manifest.Entries;
                    ArgumentNullException.ThrowIfNull(__filteredSource4353, "source");
                    foreach (var entry in __filteredSource4353)
                    {
                        if (!(!entry.IsDirectory))
                            continue;
                        var source = sources[entry.RelativePath].FullPath;
                        WriteFile(archive, entry, source, services.FileSystem);
                        Report(progress, BPackageProgressPhase.Exporting, ++exported, Matches4923(), entry.RelativePath);
                        [global::BEngine.SkipLoopGuard]
                        int Matches4923()
                        {
                            var matchSource4923 = manifest.Entries;
                            ArgumentNullException.ThrowIfNull(matchSource4923, "source");
                            int matches = 0;
                            if (matchSource4923.GetType() != typeof(global::System.Collections.Generic.List<global::BEngine.Editor.BPackageManifestEntry>))
                            {
                                foreach (var item in (global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.BPackageManifestEntry>)matchSource4923)
                                {
                                    if (!item.IsDirectory)
                                        checked
                                        {
                                            matches++;
                                        }
                                }
                        
                                return matches;
                            }
                        
                            global::System.ReadOnlySpan<global::BEngine.Editor.BPackageManifestEntry> matchValues4923 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(matchSource4923);
                            foreach (var item in matchValues4923)
                            {
                                if (!item.IsDirectory)
                                    checked
                                    {
                                        matches++;
                                    }
                            }
                        
                            return matches;
                        }
                    }
                }
            }
            services.FileSystem.MoveFile(temporaryPath, destinationPath, overwrite: true);
            Report(progress, BPackageProgressPhase.Completed, sources.Count, sources.Count, destinationPath);
            return manifest;
        }
        finally
        {
            if (services.FileSystem.FileExists(temporaryPath))
                services.FileSystem.DeleteFile(temporaryPath);
        }
    }


    internal static BPackageManifest ReadManifest(
        string archivePath,
        BPackageArchiveServices services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        using var stream = services.FileSystem.OpenRead(
            services.PathService.GetFullPath(archivePath));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Encoding.UTF8);
        var manifest = ReadManifest(archive, services.PathService);
        ValidateArchiveLayout(archive, manifest);
        return manifest;
    }


    internal static BPackageImportReceipt? ReadImportReceipt(
        ProjectWorkspace workspace,
        string installationId,
        BPackageArchiveServices services)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var normalizedId = NormalizeInstallationId(installationId);
        var path = GetReceiptPath(workspace, normalizedId, services.PathService);
        if (!services.FileSystem.FileExists(path)) return null;
        var receipt = YamlUtility.Load<BPackageImportReceipt>(
            path, services.FileSystem, services.PathService);
        ValidateReceipt(receipt, normalizedId, services.PathService);
        return receipt;
    }


    internal static BPackageImportStatus GetImportStatus(
        ProjectWorkspace workspace,
        string archivePath,
        string installationId,
        string? destinationDirectory,
        BPackageArchiveServices services)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        var normalizedId = NormalizeInstallationId(installationId);
        var receipt = ReadImportReceipt(workspace, normalizedId, services);
        var manifest = ReadManifest(archivePath, services);
        var requestedDestination = string.IsNullOrWhiteSpace(destinationDirectory)
            ? manifest.DefaultImportPath
            : destinationDirectory;
        var destination = BPackagePathUtility.NormalizeRelativePath(
            requestedDestination, services.PathService, allowEmpty: true);
        if (receipt is null)
            return GetLegacyImportStatus(workspace, manifest, destination, services);
        if (!destination.Equals(receipt.DestinationDirectory, StringComparison.OrdinalIgnoreCase))
            return new BPackageImportStatus(BPackageImportState.UpdateAvailable, receipt, [], []);

        var missing = new List<string>();
        var modified = new List<string>();
        foreach (var entry in receipt.Entries)
        {
            var relative = CombineRelative(destination, entry.RelativePath);
            var target = BPackagePathUtility.ResolveInside(
                workspace.AssetsPath, relative, services.PathService);
            var assetPath = $"Assets/{relative}";
            if (entry.IsDirectory)
            {
                if (!services.FileSystem.DirectoryExists(target)) missing.Add(assetPath);
                continue;
            }
            if (!services.FileSystem.FileExists(target))
            {
                missing.Add(assetPath);
                continue;
            }
            if (!ComputeFileHash(target, services.FileSystem)
                    .Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                modified.Add(assetPath);
        }

        var receiptPaths = BuildIndex10502(receipt.Entries);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.HashSet<string> BuildIndex10502(global::System.Collections.Generic.List<global::BEngine.Editor.BPackageImportReceiptEntry> indexSource10502)
        {
            ArgumentNullException.ThrowIfNull(indexSource10502, "source");
            var indexComparer10502 = StringComparer.OrdinalIgnoreCase;
            var result = new global::System.Collections.Generic.HashSet<string>(indexComparer10502);
            foreach (var entry in indexSource10502)
                result.Add(entry.RelativePath);
            return result;
        }
        [global::BEngine.SkipLoopGuard]
        bool Query11490(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.BPackageManifestEntry> querySource11490)
        {
            ArgumentNullException.ThrowIfNull(querySource11490, "source");
            bool Matches11490(global::BEngine.Editor.BPackageManifestEntry entry) => !receiptPaths.Contains(entry.RelativePath);
            global::System.ReadOnlySpan<global::BEngine.Editor.BPackageManifestEntry> queryValues11490;
            if (querySource11490.GetType() == typeof(global::BEngine.Editor.BPackageManifestEntry[]))
                queryValues11490 = (global::BEngine.Editor.BPackageManifestEntry[])querySource11490;
            else if (querySource11490.GetType() == typeof(global::System.Collections.Generic.List<global::BEngine.Editor.BPackageManifestEntry>))
                queryValues11490 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<global::BEngine.Editor.BPackageManifestEntry>)querySource11490);
            else
            {
                foreach (var entry in querySource11490)
                    if (Matches11490(entry))
                        return true;
                return false;
            }
        
            foreach (var entry in queryValues11490)
                if (Matches11490(entry))
                    return true;
            return false;
        }
        
        var archiveChanged = !ComputeFileHash(services.PathService.GetFullPath(archivePath), services.FileSystem).Equals(receipt.ArchiveSha256, StringComparison.OrdinalIgnoreCase) || Query11490(manifest.Entries) || receipt.Entries.Any(entry => !manifest.Entries.Any(candidate => candidate.RelativePath.Equals(entry.RelativePath, StringComparison.OrdinalIgnoreCase)));
        var state = missing.Count > 0 ? BPackageImportState.Partial
            : modified.Count > 0 ? BPackageImportState.Modified
            : archiveChanged ? BPackageImportState.UpdateAvailable
            : BPackageImportState.Installed;
        return new BPackageImportStatus(state, receipt, missing, modified);
    }

    private static BPackageImportStatus GetLegacyImportStatus(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string destination,
        BPackageArchiveServices services)
    {
        var present = 0;
        var missing = new List<string>();
        var modified = new List<string>();
        foreach (var entry in manifest.Entries)
        {
            var relative = CombineRelative(destination, entry.RelativePath);
            var target = BPackagePathUtility.ResolveInside(
                workspace.AssetsPath, relative, services.PathService);
            var assetPath = $"Assets/{relative}";
            if (entry.IsDirectory)
            {
                if (services.FileSystem.DirectoryExists(target)) present++;
                else if (services.FileSystem.FileExists(target)) { present++; modified.Add(assetPath); }
                else missing.Add(assetPath);
                continue;
            }
            if (services.FileSystem.FileExists(target))
            {
                present++;
                if (!ComputeFileHash(target, services.FileSystem)
                        .Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    modified.Add(assetPath);
            }
            else if (services.FileSystem.DirectoryExists(target)) { present++; modified.Add(assetPath); }
            else missing.Add(assetPath);
        }

        var state = present == 0 ? BPackageImportState.NotInstalled
            : modified.Count > 0 ? BPackageImportState.Modified
            : missing.Count > 0 ? BPackageImportState.Partial
            : BPackageImportState.Installed;
        return new BPackageImportStatus(state, null, missing, modified);
    }


    internal static BPackageImportResult ImportPackage(
        ProjectWorkspace workspace,
        string archivePath,
        BPackageImportOptions? options,
        Action<BPackageProgress>? progress,
        BPackageArchiveServices services)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        options ??= new BPackageImportOptions();
        ValidateImportOptions(options);
        BPackagePathUtility.EnsureNoReparsePoint(
            workspace.RootPath, workspace.TempPath, services.FileSystem, services.PathService);

        using var importLock = AcquireImportLock(workspace.TempPath, services);
        using var transaction = new BPackageImportTransaction(
            workspace.RootPath, workspace.AssetsPath, workspace.TempPath,
            services.FileSystem, services.PathService);
        var applyStarted = false;
        try
        {
            BPackageManifest manifest;
            string archiveHash;
            using (var stream = services.FileSystem.OpenFile(
                       services.PathService.GetFullPath(archivePath), FileMode.Open, FileAccess.Read,
                       FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan))
            {
                archiveHash = ComputeStreamHash(stream);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);
                manifest = ReadManifest(archive, services.PathService);
                ValidateArchiveLayout(archive, manifest);
                ValidateImportLimits(manifest, options);
                Extract(archive, manifest, transaction.StagingPath, options, progress, services);
            }

            var requestedDestination = string.IsNullOrWhiteSpace(options.DestinationDirectory)
                ? manifest.DefaultImportPath
                : options.DestinationDirectory;
            var destination = BPackagePathUtility.NormalizeRelativePath(
                requestedDestination, services.PathService, allowEmpty: true);
            var normalizedInstallationId = string.IsNullOrWhiteSpace(options.InstallationId)
                ? string.Empty
                : NormalizeInstallationId(options.InstallationId);
            var previousReceipt = normalizedInstallationId.Length == 0
                ? null
                : ReadImportReceipt(workspace, normalizedInstallationId, services);
            if (options.Mode == BPackageImportMode.Import && previousReceipt is not null)
                throw new InvalidOperationException(
                    $"Installation '{normalizedInstallationId}' is already tracked. Use reimport mode.");
            if (previousReceipt is not null &&
                !previousReceipt.DestinationDirectory.Equals(destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Installation '{normalizedInstallationId}' is tracked at " +
                    $"'Assets/{previousReceipt.DestinationDirectory}' and cannot be moved by reimport.");

            var plan = BuildImportPlan(
                workspace, manifest, destination, options, previousReceipt, services);
            ValidateStagedMetadataGuids(
                workspace, manifest, destination, transaction.StagingPath, plan, services);
            applyStarted = true;
            var (imported, skipped, unchanged, preservedModified, removed) = transaction.Apply(plan);
            Report(progress, BPackageProgressPhase.Importing, imported.Count + removed.Count,
                imported.Count + skipped.Count + unchanged.Count + preservedModified.Count + removed.Count,
                destination.Length == 0 ? "Assets" : $"Assets/{destination}");
            Refresh(workspace, progress, services.PathService);

            BPackageImportReceipt? receipt = null;
            if (normalizedInstallationId.Length > 0)
            {
                receipt = BuildReceipt(
                    workspace, manifest, archiveHash, normalizedInstallationId, plan, services);
                var stagedReceipt = services.PathService.Combine(
                    transaction.ScratchPath, "receipt.yaml");
                services.FileSystem.WriteAllText(stagedReceipt, receipt.ToYaml());
                transaction.ReplaceExternalFile(stagedReceipt,
                    GetReceiptPath(workspace, normalizedInstallationId, services.PathService));
            }
            Report(progress, BPackageProgressPhase.Completed, manifest.Entries.Count, manifest.Entries.Count,
                archivePath);
            transaction.Complete();
            return new BPackageImportResult(manifest, imported, skipped, unchanged,
                preservedModified, removed, receipt);
        }
        catch (Exception exception)
        {
            var failures = new List<Exception>();
            try { transaction.Rollback(); }
            catch (Exception rollbackException) { failures.Add(rollbackException); }
            if (applyStarted)
            {
                try { Refresh(workspace, null, services.PathService); }
                catch (Exception refreshException) { failures.Add(refreshException); }
            }
            if (failures.Count > 0)
                throw new AggregateException("Package import failed and could not be completely rolled back.",
                    new[] { exception }.Concat(failures));
            throw;
        }
    }

    private static BPackageImportPlan BuildImportPlan(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string destination,
        BPackageImportOptions options,
        BPackageImportReceipt? previousReceipt,
        BPackageArchiveServices services)
    {
        var entries = previousReceipt is not null && options.Mode == BPackageImportMode.Reimport
            ? BuildReimportEntries(workspace, manifest, destination, options, previousReceipt, services)
            : options.Mode == BPackageImportMode.Reimport
                ? BuildLegacyReimportEntries(
                    workspace, manifest, destination, options.ModifiedFilePolicy, services)
                : BuildFreshImportEntries(
                    workspace, manifest, destination, options.ConflictPolicy, services);
        PreserveDirectoriesContainingUntrackedFiles(workspace, entries, services);
        PairPreservedAssetMetadata(entries);
        return new BPackageImportPlan
        {
            DestinationDirectory = destination,
            PreviousReceipt = previousReceipt,
            Entries = entries
        };
    }

    private static List<BPackageImportPlanEntry> BuildFreshImportEntries(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string destination,
        BPackageConflictPolicy conflictPolicy,
        BPackageArchiveServices services)
    {
        var result = new List<BPackageImportPlanEntry>(manifest.Entries.Count);
        foreach (var entry in manifest.Entries)
        {
            var relative = CombineRelative(destination, entry.RelativePath);
            var target = BPackagePathUtility.ResolveInside(
                workspace.AssetsPath, relative, services.PathService);
            if (entry.IsDirectory)
            {
                if (services.FileSystem.FileExists(target))
                    throw new IOException($"A file blocks the package directory 'Assets/{relative}'.");
                result.Add(new BPackageImportPlanEntry
                {
                    RelativePath = relative,
                    SourceRelativePath = entry.RelativePath,
                    IsDirectory = true,
                    Action = services.FileSystem.DirectoryExists(target)
                        ? BPackageImportAction.Unchanged
                        : BPackageImportAction.CreateDirectory
                });
                continue;
            }

            if (services.FileSystem.DirectoryExists(target))
                throw new IOException($"A directory blocks the package file 'Assets/{relative}'.");
            var currentHash = services.FileSystem.FileExists(target)
                ? ComputeFileHash(target, services.FileSystem)
                : null;
            var action = currentHash is null
                ? BPackageImportAction.WriteFile
                : conflictPolicy switch
                {
                    BPackageConflictPolicy.Fail => throw new IOException(
                        $"The package asset already exists: 'Assets/{relative}'."),
                    BPackageConflictPolicy.Skip => BPackageImportAction.Skipped,
                    BPackageConflictPolicy.Overwrite => BPackageImportAction.WriteFile,
                    _ => throw new ArgumentOutOfRangeException(nameof(conflictPolicy), conflictPolicy, null)
                };
            result.Add(new BPackageImportPlanEntry
            {
                RelativePath = relative,
                SourceRelativePath = entry.RelativePath,
                IsDirectory = false,
                Action = action,
                ExpectedSha256 = action == BPackageImportAction.WriteFile ? currentHash : null
            });
        }
        return result;
    }

    private static List<BPackageImportPlanEntry> BuildLegacyReimportEntries(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string destination,
        BPackageModifiedFilePolicy modifiedFilePolicy,
        BPackageArchiveServices services)
    {
        var result = new List<BPackageImportPlanEntry>(manifest.Entries.Count);
        foreach (var entry in manifest.Entries)
        {
            var relative = CombineRelative(destination, entry.RelativePath);
            var target = BPackagePathUtility.ResolveInside(
                workspace.AssetsPath, relative, services.PathService);
            var baseline = new BPackageImportReceiptEntry
            {
                RelativePath = entry.RelativePath,
                Sha256 = entry.Sha256,
                IsDirectory = entry.IsDirectory
            };
            if (entry.IsDirectory)
            {
                if (services.FileSystem.FileExists(target))
                    throw new IOException($"A file blocks the package directory 'Assets/{relative}'.");
                result.Add(new BPackageImportPlanEntry
                {
                    RelativePath = relative,
                    SourceRelativePath = entry.RelativePath,
                    IsDirectory = true,
                    Action = services.FileSystem.DirectoryExists(target)
                        ? BPackageImportAction.Unchanged
                        : BPackageImportAction.CreateDirectory,
                    PreviousReceiptEntry = baseline
                });
                continue;
            }

            if (services.FileSystem.DirectoryExists(target))
                throw new IOException($"A directory blocks the package file 'Assets/{relative}'.");
            var currentHash = services.FileSystem.FileExists(target)
                ? ComputeFileHash(target, services.FileSystem)
                : null;
            var action = currentHash is null
                ? BPackageImportAction.WriteFile
                : currentHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? BPackageImportAction.Unchanged
                    : modifiedFilePolicy switch
                    {
                        BPackageModifiedFilePolicy.Preserve => BPackageImportAction.PreserveModified,
                        BPackageModifiedFilePolicy.Fail => throw new IOException(
                            $"The legacy package asset was modified: 'Assets/{relative}'."),
                        BPackageModifiedFilePolicy.Overwrite => BPackageImportAction.WriteFile,
                        _ => throw new ArgumentOutOfRangeException(nameof(modifiedFilePolicy),
                            modifiedFilePolicy, null)
                    };
            result.Add(new BPackageImportPlanEntry
            {
                RelativePath = relative,
                SourceRelativePath = entry.RelativePath,
                IsDirectory = false,
                Action = action,
                ExpectedSha256 = action == BPackageImportAction.WriteFile ? currentHash : null,
                PreviousReceiptEntry = baseline
            });
        }
        return result;
    }

    private static List<BPackageImportPlanEntry> BuildReimportEntries(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string destination,
        BPackageImportOptions options,
        BPackageImportReceipt receipt,
        BPackageArchiveServices services)
    {
        var previous = BuildIndex26021(receipt.Entries, StringComparer.OrdinalIgnoreCase);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.BPackageImportReceiptEntry> BuildIndex26021(global::System.Collections.Generic.IEnumerable<global::BEngine.Editor.BPackageImportReceiptEntry> indexSource26021, global::System.Collections.Generic.IEqualityComparer<string>? indexComparer26021)
        {
            ArgumentNullException.ThrowIfNull(indexSource26021, "source");
            var knownCount = indexSource26021.TryGetNonEnumeratedCount(out var capacity);
            if (knownCount && capacity == 0)
                return new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.BPackageImportReceiptEntry>(indexComparer26021);
            global::System.ReadOnlySpan<global::BEngine.Editor.BPackageImportReceiptEntry> values;
            if (knownCount && indexSource26021 is global::BEngine.Editor.BPackageImportReceiptEntry[] array)
                values = array;
            else if (knownCount && indexSource26021 is global::System.Collections.Generic.List<global::BEngine.Editor.BPackageImportReceiptEntry> list)
                values = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);
            else
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.BPackageImportReceiptEntry>(capacity, indexComparer26021);
                foreach (var item in indexSource26021)
                    result.Add(IndexKey26021(item), item);
                return result;
            }
        
            {
                var result = new global::System.Collections.Generic.Dictionary<string, global::BEngine.Editor.BPackageImportReceiptEntry>(values.Length, indexComparer26021);
                foreach (var item in values)
                    result.Add(IndexKey26021(item), item);
                return result;
            }
        
            string IndexKey26021(global::BEngine.Editor.BPackageImportReceiptEntry entry) => entry.RelativePath;
        }
        var currentPaths = BuildIndex26153(manifest.Entries);
        [global::BEngine.SkipLoopGuard]
        global::System.Collections.Generic.HashSet<string> BuildIndex26153(global::System.Collections.Generic.List<global::BEngine.Editor.BPackageManifestEntry> indexSource26153)
        {
            ArgumentNullException.ThrowIfNull(indexSource26153, "source");
            var indexComparer26153 = StringComparer.OrdinalIgnoreCase;
            var result = new global::System.Collections.Generic.HashSet<string>(indexComparer26153);
            foreach (var entry in indexSource26153)
                result.Add(entry.RelativePath);
            return result;
        }
        var result = new List<BPackageImportPlanEntry>(manifest.Entries.Count + receipt.Entries.Count);

        foreach (var entry in manifest.Entries)
        {
            previous.TryGetValue(entry.RelativePath, out var oldEntry);
            if (oldEntry is not null && oldEntry.IsDirectory != entry.IsDirectory)
                throw new InvalidDataException(
                    $"Tracked package entry changed between file and directory: '{entry.RelativePath}'.");
            var relative = CombineRelative(destination, entry.RelativePath);
            var target = BPackagePathUtility.ResolveInside(
                workspace.AssetsPath, relative, services.PathService);
            if (entry.IsDirectory)
            {
                if (services.FileSystem.FileExists(target))
                    throw new IOException($"A file blocks the package directory 'Assets/{relative}'.");
                result.Add(new BPackageImportPlanEntry
                {
                    RelativePath = relative,
                    SourceRelativePath = entry.RelativePath,
                    IsDirectory = true,
                    Action = services.FileSystem.DirectoryExists(target)
                        ? BPackageImportAction.Unchanged
                        : BPackageImportAction.CreateDirectory,
                    PreviousReceiptEntry = oldEntry
                });
                continue;
            }

            if (services.FileSystem.DirectoryExists(target))
                throw new IOException($"A directory blocks the package file 'Assets/{relative}'.");
            var currentHash = services.FileSystem.FileExists(target)
                ? ComputeFileHash(target, services.FileSystem)
                : null;
            BPackageImportAction action;
            if (currentHash is null)
            {
                action = BPackageImportAction.WriteFile;
            }
            else if (currentHash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                action = BPackageImportAction.Unchanged;
            }
            else if (oldEntry is not null &&
                     currentHash.Equals(oldEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                action = BPackageImportAction.WriteFile;
            }
            else if (oldEntry is not null)
            {
                action = options.ModifiedFilePolicy switch
                {
                    BPackageModifiedFilePolicy.Preserve => BPackageImportAction.PreserveModified,
                    BPackageModifiedFilePolicy.Fail => throw new IOException(
                        $"The tracked package asset was modified: 'Assets/{relative}'."),
                    BPackageModifiedFilePolicy.Overwrite => BPackageImportAction.WriteFile,
                    _ => throw new ArgumentOutOfRangeException(nameof(options.ModifiedFilePolicy),
                        options.ModifiedFilePolicy, null)
                };
            }
            else
            {
                action = options.ConflictPolicy switch
                {
                    BPackageConflictPolicy.Fail => throw new IOException(
                        $"An untracked asset blocks package reimport: 'Assets/{relative}'."),
                    BPackageConflictPolicy.Skip => BPackageImportAction.Skipped,
                    BPackageConflictPolicy.Overwrite => BPackageImportAction.WriteFile,
                    _ => throw new ArgumentOutOfRangeException(nameof(options.ConflictPolicy),
                        options.ConflictPolicy, null)
                };
            }

            result.Add(new BPackageImportPlanEntry
            {
                RelativePath = relative,
                SourceRelativePath = entry.RelativePath,
                IsDirectory = false,
                Action = action,
                ExpectedSha256 = action == BPackageImportAction.WriteFile ? currentHash : null,
                PreviousReceiptEntry = oldEntry
            });
        }

        {
            var __filteredSource28359 = receipt.Entries;
            ArgumentNullException.ThrowIfNull(__filteredSource28359, "source");
            foreach (var oldEntry in __filteredSource28359)
            {
                if (!(!currentPaths.Contains(oldEntry.RelativePath)))
                    continue;
                var relative = CombineRelative(destination, oldEntry.RelativePath);
                var target = BPackagePathUtility.ResolveInside(workspace.AssetsPath, relative, services.PathService);
                if (oldEntry.IsDirectory)
                {
                    if (services.FileSystem.FileExists(target))
                    {
                        result.Add(PreservedObsolete(relative, oldEntry));
                    }
                    else if (services.FileSystem.DirectoryExists(target))
                    {
                        result.Add(new BPackageImportPlanEntry { RelativePath = relative, IsDirectory = true, Action = BPackageImportAction.DeleteDirectory, PreviousReceiptEntry = oldEntry });
                    }
        
                    continue;
                }
        
                if (services.FileSystem.DirectoryExists(target))
                {
                    result.Add(PreservedObsolete(relative, oldEntry));
                    continue;
                }
        
                if (!services.FileSystem.FileExists(target))
                    continue;
                var currentHash = ComputeFileHash(target, services.FileSystem);
                var action = currentHash.Equals(oldEntry.Sha256, StringComparison.OrdinalIgnoreCase) ? BPackageImportAction.DeleteFile : options.ModifiedFilePolicy switch
                {
                    BPackageModifiedFilePolicy.Preserve => BPackageImportAction.PreserveModified,
                    BPackageModifiedFilePolicy.Fail => throw new IOException($"The obsolete tracked package asset was modified: 'Assets/{relative}'."),
                    BPackageModifiedFilePolicy.Overwrite => BPackageImportAction.DeleteFile,
                    _ => throw new ArgumentOutOfRangeException(nameof(options.ModifiedFilePolicy), options.ModifiedFilePolicy, null)};
                result.Add(new BPackageImportPlanEntry { RelativePath = relative, IsDirectory = false, Action = action, ExpectedSha256 = currentHash, PreviousReceiptEntry = oldEntry });
            }
        }
        return result;
    }

    private static BPackageImportPlanEntry PreservedObsolete(
        string relative,
        BPackageImportReceiptEntry oldEntry) => new()
    {
        RelativePath = relative,
        IsDirectory = oldEntry.IsDirectory,
        Action = BPackageImportAction.PreserveModified,
        PreviousReceiptEntry = oldEntry
    };

    private static void PairPreservedAssetMetadata(IReadOnlyList<BPackageImportPlanEntry> entries)
    {
        var byPath = entries.ToDictionary(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries.Where(entry => entry.Action is
                     BPackageImportAction.Skipped or BPackageImportAction.PreserveModified).ToArray())
        {
            var partnerPath = entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? entry.RelativePath[..^".meta".Length]
                : entry.RelativePath + ".meta";
            if (!byPath.TryGetValue(partnerPath, out var partner)) continue;
            if (entry.Action == BPackageImportAction.PreserveModified &&
                partner.Action == BPackageImportAction.WriteFile && partner.ExpectedSha256 is null)
                continue;
            var action = entry.Action == BPackageImportAction.PreserveModified ||
                         partner.Action == BPackageImportAction.PreserveModified
                ? BPackageImportAction.PreserveModified
                : BPackageImportAction.Skipped;
            entry.Action = action;
            partner.Action = action;
        }
    }

    private static void PreserveDirectoriesContainingUntrackedFiles(
        ProjectWorkspace workspace,
        IReadOnlyList<BPackageImportPlanEntry> entries,
        BPackageArchiveServices services)
    {
        var deletions = entries.Where(entry => entry.Action is
                BPackageImportAction.DeleteFile or BPackageImportAction.DeleteDirectory)
            .Select(entry => entry.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        {
            var __filteredSource32820 = entries;
            ArgumentNullException.ThrowIfNull(__filteredSource32820, "source");
            foreach (var entry in __filteredSource32820)
            {
                if (!(entry.Action == BPackageImportAction.DeleteDirectory))
                    continue;
                var directory = BPackagePathUtility.ResolveInside(workspace.AssetsPath, entry.RelativePath, services.PathService);
                if (!services.FileSystem.DirectoryExists(directory))
                    continue;
                [global::BEngine.SkipLoopGuard]
                bool Query38487(global::System.Collections.Generic.IEnumerable<string> querySource38487)
                {
                    ArgumentNullException.ThrowIfNull(querySource38487, "source");
                    bool Matches38487(string path) => !deletions.Contains(path);
                    global::System.ReadOnlySpan<string> queryValues38487;
                    if (querySource38487.GetType() == typeof(string[]))
                        queryValues38487 = (string[])querySource38487;
                    else if (querySource38487.GetType() == typeof(global::System.Collections.Generic.List<string>))
                        queryValues38487 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)querySource38487);
                    else
                    {
                        foreach (var path in querySource38487)
                            if (Matches38487(path))
                                return true;
                        return false;
                    }
                
                    foreach (var path in queryValues38487)
                        if (Matches38487(path))
                            return true;
                    return false;
                }
                
                var hasUntrackedContent = Query38487(EnumerateFileSystemTree(directory, services.FileSystem).Select(path => services.PathService.GetRelativePath(workspace.AssetsPath, path).Replace('\\', '/')));
                if (hasUntrackedContent)
                    entry.Action = BPackageImportAction.PreserveModified;
            }
        }
    }

    private static IEnumerable<string> EnumerateFileSystemTree(
        string directory,
        FileUtility fileSystem) =>
        fileSystem.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Concat(fileSystem.EnumerateDirectories(
                directory, "*", SearchOption.AllDirectories));

    private static BPackageImportReceipt BuildReceipt(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string archiveHash,
        string installationId,
        BPackageImportPlan plan,
        BPackageArchiveServices services)
    {
        var planned = plan.Entries.Where(entry => entry.SourceRelativePath is not null)
            .ToDictionary(entry => entry.SourceRelativePath!, StringComparer.OrdinalIgnoreCase);
        var entries = new List<BPackageImportReceiptEntry>();
        foreach (var manifestEntry in manifest.Entries)
        {
            var planEntry = planned[manifestEntry.RelativePath];
            if (planEntry.Action is BPackageImportAction.Skipped or BPackageImportAction.PreserveModified)
            {
                if (planEntry.PreviousReceiptEntry is { } previous)
                    entries.Add(CloneReceiptEntry(previous, manifestEntry.RelativePath));
                continue;
            }

            var relative = CombineRelative(plan.DestinationDirectory, manifestEntry.RelativePath);
            var target = BPackagePathUtility.ResolveInside(
                workspace.AssetsPath, relative, services.PathService);
            if (manifestEntry.IsDirectory)
            {
                if (!services.FileSystem.DirectoryExists(target))
                    throw new IOException($"Imported package directory is missing: 'Assets/{relative}'.");
                entries.Add(new BPackageImportReceiptEntry
                {
                    RelativePath = manifestEntry.RelativePath,
                    IsDirectory = true
                });
            }
            else
            {
                if (!services.FileSystem.FileExists(target))
                    throw new IOException($"Imported package asset is missing: 'Assets/{relative}'.");
                entries.Add(new BPackageImportReceiptEntry
                {
                    RelativePath = manifestEntry.RelativePath,
                    Sha256 = ComputeFileHash(target, services.FileSystem)
                });
            }
        }

        return new BPackageImportReceipt
        {
            InstallationId = installationId,
            PackageName = manifest.Name,
            PackageVersion = manifest.PackageVersion,
            DestinationDirectory = plan.DestinationDirectory,
            ArchiveSha256 = archiveHash,
            Entries = entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToList()
        };
    }

    private static BPackageImportReceiptEntry CloneReceiptEntry(
        BPackageImportReceiptEntry entry,
        string relativePath) => new()
    {
        RelativePath = relativePath,
        Sha256 = entry.Sha256,
        IsDirectory = entry.IsDirectory
    };

    private static void ValidateStagedMetadataGuids(
        ProjectWorkspace workspace,
        BPackageManifest manifest,
        string destination,
        string stagingPath,
        BPackageImportPlan plan,
        BPackageArchiveServices services)
    {
        var existing = new Dictionary<Guid, string>();
        foreach (var metadataPath in services.FileSystem.EnumerateFiles(workspace.AssetsPath, "*.meta",
                     SearchOption.AllDirectories))
        {
            var metadata = LoadAssetMetadata(metadataPath, services);
            var guid = Guid.Parse(metadata.Guid);
            var assetPath = metadataPath[..^".meta".Length];
            if (existing.TryGetValue(guid, out var duplicate) &&
                !duplicate.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Project assets '{duplicate}' and '{assetPath}' share GUID '{guid:N}'.");
            existing[guid] = assetPath;
        }

        var staged = new Dictionary<Guid, string>();
        var writtenMetadata = plan.Entries.Where(entry => entry.Action == BPackageImportAction.WriteFile &&
                entry.SourceRelativePath is not null &&
                entry.SourceRelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.SourceRelativePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        {
            var __filteredSource38075 = manifest.Entries;
            ArgumentNullException.ThrowIfNull(__filteredSource38075, "source");
            foreach (var entry in __filteredSource38075)
            {
                if (!(!entry.IsDirectory && entry.RelativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
                    continue;
                var metadataPath = BPackagePathUtility.ResolveInside(stagingPath, entry.RelativePath, services.PathService);
                var metadata = LoadAssetMetadata(metadataPath, services);
                var guid = Guid.Parse(metadata.Guid);
                var assetRelative = entry.RelativePath[..^".meta".Length];
                if (assetRelative.Length == 0)
                    throw new InvalidDataException("A package metadata entry has no matching asset path.");
                var targetAsset = BPackagePathUtility.ResolveInside(workspace.AssetsPath, CombineRelative(destination, assetRelative), services.PathService);
                if (staged.TryGetValue(guid, out var stagedOwner) && !stagedOwner.Equals(targetAsset, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package assets '{stagedOwner}' and '{targetAsset}' share GUID '{guid:N}'.");
                if (writtenMetadata.Contains(entry.RelativePath) && existing.TryGetValue(guid, out var existingOwner) && !existingOwner.Equals(targetAsset, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package GUID '{guid:N}' is already used by '{existingOwner}'.");
                var targetMetadata = targetAsset + ".meta";
                if (writtenMetadata.Contains(entry.RelativePath) && services.FileSystem.FileExists(targetMetadata))
                {
                    var currentMetadata = LoadAssetMetadata(targetMetadata, services);
                    if (!currentMetadata.Guid.Equals(metadata.Guid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Package reimport cannot change the GUID of '{targetAsset}'.");
                }
        
                staged[guid] = targetAsset;
            }
        }
    }

    private static BEngine.ProjectSystem.Editor.AssetMetaDocument LoadAssetMetadata(
        string path,
        BPackageArchiveServices services)
    {
        BEngine.ProjectSystem.Editor.AssetMetaDocument metadata;
        try
        {
            metadata = YamlUtility.Load<BEngine.ProjectSystem.Editor.AssetMetaDocument>(
                path, services.FileSystem, services.PathService);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or FormatException)
        {
            throw new InvalidDataException($"Invalid asset metadata '{path}'.", exception);
        }
        if (!metadata.Format.Equals("BEngine.AssetMeta", StringComparison.Ordinal) || metadata.Version != 1 ||
            !Guid.TryParse(metadata.Guid, out _))
            throw new InvalidDataException($"Invalid asset metadata '{path}'.");
        return metadata;
    }

    private static string GetReceiptPath(
        ProjectWorkspace workspace,
        string installationId,
        PathUtility pathService)
    {
        var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(installationId)));
        return pathService.Combine(workspace.ProjectSettingsPath, "BPackageImports", key + ".yaml");
    }

    private static void ValidateReceipt(
        BPackageImportReceipt receipt,
        string installationId,
        PathUtility pathService)
    {
        if (!receipt.Format.Equals("BEngine.BPackageImportReceipt", StringComparison.Ordinal) ||
            receipt.Version != 1)
            throw new InvalidDataException(
                $"Unsupported package import receipt '{receipt.Format}' v{receipt.Version}.");
        if (!receipt.InstallationId.Equals(installationId, StringComparison.Ordinal))
            throw new InvalidDataException("The package import receipt installation id does not match its file.");
        var destination = BPackagePathUtility.NormalizeRelativePath(
            receipt.DestinationDirectory, pathService, allowEmpty: true);
        if (!destination.Equals(receipt.DestinationDirectory, StringComparison.Ordinal))
            throw new InvalidDataException("The package import receipt destination is not canonical.");
        if (!IsSha256(receipt.ArchiveSha256) || receipt.Entries is null)
            throw new InvalidDataException("The package import receipt is incomplete.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in receipt.Entries)
        {
            if (entry is null || entry.RelativePath is null || entry.Sha256 is null)
                throw new InvalidDataException("The package import receipt contains an incomplete entry.");
            var relative = BPackagePathUtility.NormalizeRelativePath(entry.RelativePath, pathService);
            if (!relative.Equals(entry.RelativePath, StringComparison.Ordinal) || !paths.Add(relative) ||
                entry.IsDirectory && entry.Sha256.Length != 0 || !entry.IsDirectory && !IsSha256(entry.Sha256))
                throw new InvalidDataException(
                    $"Invalid package import receipt entry '{entry.RelativePath}'.");
        }
        receipt.Entries = receipt.Entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToList();
    }

    private static string NormalizeInstallationId(string installationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationId);
        var normalized = installationId.Trim();
        if (normalized.Length > 256 || normalized.Any(char.IsControl))
            throw new ArgumentException("The package installation id is invalid.", nameof(installationId));
        return normalized;
    }

    private static string CombineRelative(string destination, string relativePath) =>
        destination.Length == 0 ? relativePath : $"{destination}/{relativePath}";

    private static string ComputeFileHash(string path, FileUtility fileSystem)
    {
        using var stream = fileSystem.OpenRead(path);
        return ComputeStreamHash(stream);
    }

    private static string ComputeStreamHash(Stream stream)
    {
        if (stream.CanSeek) stream.Position = 0;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (stream.CanSeek) stream.Position = 0;
        return hash;
    }

    private static SortedDictionary<string, (string FullPath, bool IsDirectory)> CollectSources(
        ProjectWorkspace workspace,
        IEnumerable<string> paths,
        bool includeMetaFiles,
        string destinationPath,
        BPackageArchiveServices services)
    {
        var sources = new SortedDictionary<string, (string FullPath, bool IsDirectory)>(StringComparer.Ordinal);
        {
            var __filteredSource44898 = paths;
            ArgumentNullException.ThrowIfNull(__filteredSource44898, "source");
            foreach (var requestedPath in __filteredSource44898)
            {
                if (!(!string.IsNullOrWhiteSpace(requestedPath)))
                    continue;
                var source = BPackagePathUtility.ResolveAssetSource(workspace.AssetsPath, requestedPath, services.PathService);
                if (!services.FileSystem.FileExists(source) && !services.FileSystem.DirectoryExists(source))
                    throw new FileNotFoundException("The selected package asset does not exist.", source);
                CollectSourceTree(workspace.AssetsPath, source, includeMetaFiles, sources, services);
                if (includeMetaFiles && !source.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && services.FileSystem.FileExists(source + ".meta"))
                {
                    CollectSourceTree(workspace.AssetsPath, source + ".meta", true, sources, services);
                }
            }
        }

        if (destinationPath.Equals(workspace.AssetsPath, StringComparison.OrdinalIgnoreCase) ||
            destinationPath.StartsWith(
                workspace.AssetsPath + services.PathService.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            var relativeDestination = BPackagePathUtility.ToAssetRelativePath(
                workspace.AssetsPath, destinationPath, services.PathService);
            sources.Remove(relativeDestination);
            sources.Remove(relativeDestination + ".meta");
        }
        return sources;
    }

    private static void CollectSourceTree(
        string assetsRoot,
        string source,
        bool includeMetaFiles,
        IDictionary<string, (string FullPath, bool IsDirectory)> sources,
        BPackageArchiveServices services)
    {
        BPackagePathUtility.EnsureNoReparsePoint(
            assetsRoot, source, services.FileSystem, services.PathService);
        var isDirectory = services.FileSystem.DirectoryExists(source);
        if (!isDirectory && !includeMetaFiles && source.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
        var relative = BPackagePathUtility.ToAssetRelativePath(
            assetsRoot, source, services.PathService);
        if (relative.Length > 0) sources[relative] = (source, isDirectory);
        if (!isDirectory) return;

        foreach (var child in services.FileSystem.EnumerateFileSystemEntries(source)
                     .OrderBy(path => services.PathService.GetFileName(path), StringComparer.Ordinal))
        {
            CollectSourceTree(assetsRoot, child, includeMetaFiles, sources, services);
        }
    }

    private static BPackageManifestEntry CreateFileManifestEntry(
        string relativePath,
        string source,
        FileUtility fileSystem)
    {
        using var stream = fileSystem.OpenFile(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.SequentialScan);
        var length = stream.Length;
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (stream.Length != length) throw new IOException($"Asset changed while it was exported: '{source}'.");
        return new BPackageManifestEntry { RelativePath = relativePath, Sha256 = hash, Size = length };
    }

    private static void WriteManifest(ZipArchive archive, BPackageManifest manifest)
    {
        var entry = CreateEntry(archive, BPackageArchiveFormat.ManifestPath, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096, leaveOpen: false);
        writer.Write(manifest.ToYaml().Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    private static void WriteFile(
        ZipArchive archive,
        BPackageManifestEntry manifestEntry,
        string source,
        FileUtility fileSystem)
    {
        var entry = CreateEntry(archive, BPackageArchiveFormat.PayloadPrefix + manifestEntry.RelativePath,
            CompressionLevel.Optimal);
        using var input = fileSystem.OpenFile(
            source, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, FileOptions.SequentialScan);
        using var output = entry.Open();
        var (length, hash) = CopyAndHash(input, output, manifestEntry.Size);
        if (length != manifestEntry.Size || !hash.Equals(manifestEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Asset changed while it was exported: '{source}'.");
    }

    private static ZipArchiveEntry CreateEntry(ZipArchive archive, string path, CompressionLevel compressionLevel)
    {
        var entry = archive.CreateEntry(path, compressionLevel);
        entry.LastWriteTime = BPackageArchiveFormat.DeterministicTimestamp;
        entry.ExternalAttributes = 0;
        return entry;
    }

    private static BPackageManifest ReadManifest(
        ZipArchive archive,
        PathUtility pathService)
    {
        var entries = archive.Entries.Where(entry =>
            entry.FullName.Equals(BPackageArchiveFormat.ManifestPath, StringComparison.Ordinal)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException("The package must contain one manifest.yaml file.");
        if (entries[0].Length > BPackageArchiveFormat.MaximumManifestBytes)
            throw new InvalidDataException("The package manifest is too large.");
        using var stream = entries[0].Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: false);
        var manifest = YamlUtility.Deserialize<BPackageManifest>(reader.ReadToEnd());
        ValidateManifest(manifest, pathService);
        return manifest;
    }

    private static void ValidateManifest(BPackageManifest manifest, PathUtility pathService)
    {
        if (!string.Equals(manifest.Format, BPackageArchiveFormat.Format, StringComparison.Ordinal) ||
            manifest.Version != BPackageArchiveFormat.Version)
        {
            throw new InvalidDataException(
                $"Unsupported package archive '{manifest.Format}' v{manifest.Version}.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.PackageVersion))
            throw new InvalidDataException("Package name and version cannot be empty.");
        if (manifest.Entries is null) throw new InvalidDataException("The package entry list is missing.");

        var paths = new Dictionary<string, BPackageManifestEntry>(StringComparer.OrdinalIgnoreCase);
        long totalSize = 0;
        foreach (var entry in manifest.Entries)
        {
            if (entry is null) throw new InvalidDataException("The package contains an empty manifest entry.");
            if (entry.RelativePath is null || entry.Sha256 is null)
                throw new InvalidDataException("The package contains an incomplete manifest entry.");
            var normalized = BPackagePathUtility.NormalizeRelativePath(
                entry.RelativePath, pathService);
            if (!entry.RelativePath.Equals(normalized, StringComparison.Ordinal))
                throw new InvalidDataException($"Package path is not canonical: '{entry.RelativePath}'.");
            if (!paths.TryAdd(normalized, entry))
                throw new InvalidDataException($"The package contains a duplicate path: '{normalized}'.");
            if (entry.Size < 0 || entry.IsDirectory && (entry.Size != 0 || entry.Sha256.Length != 0) ||
                !entry.IsDirectory && !IsSha256(entry.Sha256))
            {
                throw new InvalidDataException($"The package entry metadata is invalid: '{normalized}'.");
            }
            if (!entry.IsDirectory)
            {
                try { totalSize = checked(totalSize + entry.Size); }
                catch (OverflowException)
                {
                    throw new InvalidDataException("The package total size is invalid.");
                }
            }
        }

        foreach (var (path, entry) in paths)
        {
            for (var parent = pathService.GetDirectoryName(
                     path.Replace('/', pathService.DirectorySeparatorChar));
                 !string.IsNullOrEmpty(parent);
                 parent = pathService.GetDirectoryName(parent))
            {
                var normalizedParent = parent.Replace('\\', '/');
                if (paths.TryGetValue(normalizedParent, out var parentEntry) && !parentEntry.IsDirectory)
                    throw new InvalidDataException($"A package file contains another entry: '{entry.RelativePath}'.");
            }
        }
        manifest.Entries = manifest.Entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToList();
    }

    private static void ValidateArchiveLayout(
        ZipArchive archive,
        BPackageManifest manifest)
    {
        var expected = manifest.Entries.Where(entry => !entry.IsDirectory).ToDictionary(
            entry => BPackageArchiveFormat.PayloadPrefix + entry.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var actual = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Equals(BPackageArchiveFormat.ManifestPath, StringComparison.Ordinal)) continue;
            if (IsLink(entry))
                throw new InvalidDataException($"Package links are not supported: '{entry.FullName}'.");
            if (!expected.TryGetValue(entry.FullName, out var manifestEntry) ||
                !entry.FullName.Equals(BPackageArchiveFormat.PayloadPrefix + manifestEntry.RelativePath,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected package archive entry: '{entry.FullName}'.");
            }
            if (!actual.TryAdd(entry.FullName, entry))
                throw new InvalidDataException($"Duplicate package archive entry: '{entry.FullName}'.");
            if (entry.Length != manifestEntry.Size)
                throw new InvalidDataException($"Package entry size does not match its manifest: '{entry.FullName}'.");
        }
        var missing = Matches55886();
        [global::BEngine.SkipLoopGuard]
        string? Matches55886()
        {
            var matchSource55886 = expected.Keys;
            ArgumentNullException.ThrowIfNull(matchSource55886, "source");
            global::System.ReadOnlySpan<string> matchValues55886;
            if (matchSource55886.GetType() == typeof(string[]))
            {
                matchValues55886 = (string[])(object)matchSource55886;
            }
            else if (matchSource55886.GetType() == typeof(global::System.Collections.Generic.List<string>))
            {
                matchValues55886 = global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan((global::System.Collections.Generic.List<string>)(object)matchSource55886);
            }
            else
            {
                foreach (var path in (global::System.Collections.Generic.IEnumerable<string>)matchSource55886)
                {
                    if (!actual.ContainsKey(path))
                        return path;
                }
        
                return default!;
            }
        
            foreach (var path in matchValues55886)
            {
                if (!actual.ContainsKey(path))
                    return path;
            }
        
            return default!;
        }
        if (missing is not null) throw new InvalidDataException($"Package archive entry is missing: '{missing}'.");
    }

    private static void ValidateImportOptions(BPackageImportOptions options)
    {
        if (options.MaximumEntryCount <= 0 || options.MaximumFileSize < 0 || options.MaximumTotalSize < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Package import limits must be positive.");
        if (!Enum.IsDefined(options.ConflictPolicy))
            throw new ArgumentOutOfRangeException(nameof(options), "The package conflict policy is invalid.");
        if (!Enum.IsDefined(options.Mode))
            throw new ArgumentOutOfRangeException(nameof(options), "The package import mode is invalid.");
        if (!Enum.IsDefined(options.ModifiedFilePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), "The modified file policy is invalid.");
        if (options.Mode == BPackageImportMode.Reimport && string.IsNullOrWhiteSpace(options.InstallationId))
            throw new ArgumentException("Tracked reimport requires a stable installation id.", nameof(options));
        if (!string.IsNullOrWhiteSpace(options.InstallationId))
            _ = NormalizeInstallationId(options.InstallationId);
    }

    private static void ValidateImportLimits(BPackageManifest manifest, BPackageImportOptions options)
    {
        if (manifest.Entries.Count > options.MaximumEntryCount)
            throw new InvalidDataException("The package contains too many entries.");
        long total = 0;
        {
            var __filteredSource56814 = manifest.Entries;
            ArgumentNullException.ThrowIfNull(__filteredSource56814, "source");
            foreach (var entry in __filteredSource56814)
            {
                if (!(!entry.IsDirectory))
                    continue;
                if (entry.Size > options.MaximumFileSize)
                    throw new InvalidDataException($"Package entry exceeds the file size limit: '{entry.RelativePath}'.");
                try
                {
                    total = checked(total + entry.Size);
                }
                catch (OverflowException)
                {
                    throw new InvalidDataException("The package total size is invalid.");
                }
        
                if (total > options.MaximumTotalSize)
                    throw new InvalidDataException("The package exceeds the total size limit.");
            }
        }
    }

    private static void Extract(
        ZipArchive archive,
        BPackageManifest manifest,
        string stagingRoot,
        BPackageImportOptions options,
        Action<BPackageProgress>? progress,
        BPackageArchiveServices services)
    {
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        var completed = 0;
        foreach (var manifestEntry in manifest.Entries)
        {
            var staged = BPackagePathUtility.ResolveInside(
                stagingRoot, manifestEntry.RelativePath, services.PathService);
            if (manifestEntry.IsDirectory)
            {
                services.FileSystem.CreateDirectory(staged);
            }
            else
            {
                services.FileSystem.CreateDirectory(
                    services.PathService.GetDirectoryName(staged)!);
                var archiveEntry = entries[BPackageArchiveFormat.PayloadPrefix + manifestEntry.RelativePath];
                using var input = archiveEntry.Open();
                using var output = services.FileSystem.OpenFile(
                    staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 4096, FileOptions.SequentialScan);
                var (length, hash) = CopyAndHash(input, output, options.MaximumFileSize);
                if (length != manifestEntry.Size || options.VerifyHashes &&
                    !hash.Equals(manifestEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"Package entry checksum failed: '{manifestEntry.RelativePath}'.");
                }
            }
            Report(progress, BPackageProgressPhase.Extracting, ++completed, manifest.Entries.Count,
                manifestEntry.RelativePath);
        }
    }

    private static (long Length, string Sha256) CopyAndHash(Stream input, Stream output, long maximumLength)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        long total = 0;
        try
        {
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                total = checked(total + read);
                if (total > maximumLength) throw new InvalidDataException("Package entry exceeds its size limit.");
                hash.AppendData(buffer, 0, read);
                output.Write(buffer, 0, read);
            }
            return (total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void Refresh(
        ProjectWorkspace workspace,
        Action<BPackageProgress>? progress,
        PathUtility pathService)
    {
        Report(progress, BPackageProgressPhase.Refreshing, 0, 1, "Assets");
        var activeProjectPath = EditorApplication.projectPath;
        if (!string.IsNullOrEmpty(activeProjectPath) &&
            pathService.GetFullPath(activeProjectPath)
                .Equals(workspace.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            AssetDatabase.Refresh();
        }
        else
        {
            new BEngine.ProjectSystem.Editor.AssetDatabase(workspace).Refresh();
        }
        Report(progress, BPackageProgressPhase.Refreshing, 1, 1, "Assets");
    }

    private static Stream AcquireImportLock(
        string temporaryRoot,
        BPackageArchiveServices services)
    {
        services.FileSystem.CreateDirectory(temporaryRoot);
        var path = services.PathService.Combine(temporaryRoot, ".bpackage-import.lock");
        var deadline = services.PlatformService.TickCount64 + 30_000;
        while (true)
        {
            try
            {
                return services.FileSystem.OpenFile(
                    path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    bufferSize: 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (services.PlatformService.TickCount64 < deadline)
            {
                services.WaitService.Wait(TimeSpan.FromMilliseconds(20));
            }
        }
    }

    private static bool IsSha256(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool IsLink(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & unixFileTypeMask;
        return unixMode == unixSymbolicLink ||
               (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static void Report(
        Action<BPackageProgress>? progress,
        BPackageProgressPhase phase,
        int completed,
        int total,
        string path) => EditorCallbacks.Dispatch(progress,
        new BPackageProgress(phase, completed, total, path), "BPackageArchive.progress");
}
