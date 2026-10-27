using System.Buffers;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using YamlDotNet.Core;

namespace BEngine.AssetBundles;

public sealed partial class AssetBundleManager
{
    private bool _localInstallInProgress;
    private bool _localInstallUsesAsyncIo;
    private long _localInstallSliceStarted;
    private int LocalInstallBatchSize => _localInstallUsesAsyncIo ? 8 : 1;
    // Installation runs before bundle loading. Give its memory I/O enough work
    // per browser turn without consuming more than half the target frame period.
    private static double LocalInstallBrowserSliceMilliseconds =>
        Application.targetFrameRate > 0 ? Math.Min(4, 500d / Application.targetFrameRate) : 4;

    public async BValueTask<AssetBundleInstallResult> InstallBuiltInReleaseAsync(
        string releaseDirectory,
        IProgress<AssetBundleInstallProgress>? progress = null,
        BCancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(releaseDirectory);
        ThrowIfDisposed();
        if (!_options.UsePersistentCache)
            throw new InvalidOperationException("Local AssetBundle installation requires a persistent cache.");
        using (await EnterStateGateAsync(cancellationToken))
        {
            ThrowIfDisposed();
            if (_loadedBundles.Count != 0)
                throw new InvalidOperationException(
                    "Install the built-in release before loading AssetBundles, or unload all unused bundles first.");
            _localInstallInProgress = true;
            try
            {
                _localInstallUsesAsyncIo = !RuntimeServiceAccess.PlatformService.IsBrowser;
                _localInstallSliceStarted = Stopwatch.GetTimestamp();
                return await InstallLocalReleaseCoreAsync(releaseDirectory, progress, cancellationToken);
            }
            finally
            {
                _localInstallInProgress = false;
            }
        }
    }

    private async BValueTask<AssetBundleInstallResult> InstallLocalReleaseCoreAsync(
        string releaseDirectory,
        IProgress<AssetBundleInstallProgress>? progress,
        BCancellationToken cancellationToken)
    {
        var root = _pathService.GetFullPath(releaseDirectory);
        var source = await ReadLocalReleaseAsync(root, cancellationToken);
        var sourceObjects = new Dictionary<string, AssetBundleDescriptor>(StringComparer.OrdinalIgnoreCase);
        var sourceBytes = source.Catalog.Bundles.Aggregate(0L, static (sum, item) => checked(sum + item.Size));
        long verifiedBytes = 0;
        var verified = 0;
        progress?.Report(new AssetBundleInstallProgress(
            AssetBundleInstallPhase.Verifying, 0, source.Catalog.Bundles.Count, 0, sourceBytes));
        // Validate the immutable release even when every destination object is already cached.
        // This prevents a damaged application install from silently becoming the new baseline.
        await VerifyLocalBatchAsync(root, source.Catalog.Bundles, cancellationToken, descriptor =>
        {
            sourceObjects.Add(descriptor.Sha256, descriptor);
            verifiedBytes += descriptor.Size;
            progress?.Report(new AssetBundleInstallProgress(AssetBundleInstallPhase.Verifying,
                ++verified, source.Catalog.Bundles.Count, verifiedBytes, sourceBytes, descriptor.Name));
        });

        var installed = await TryReadInstalledMetadataAsync(ActivePointerPath, cancellationToken);
        var previous = await TryReadInstalledMetadataAsync(PreviousPointerPath, cancellationToken);
        var objectValidity = await PreloadLocalObjectValidityAsync(source, installed, previous, cancellationToken);
        var selected = source;
        IReadOnlyList<AssetBundleDescriptor> selectedBundles = source.Catalog.Bundles;
        var preservedNewer = false;
        foreach (var candidate in new[] { installed, previous })
        {
            if (candidate is null || !(IsNewerLocalRelease(candidate.Version.Version, source.Version.Version) ||
                    (candidate.Catalog.IsPackageComposite && candidate.Version.Version.Equals(
                        source.Version.Version, StringComparison.Ordinal))))
                continue;
            var bundles = SelectInstalledLocalBundles(candidate.Catalog);
            if (!CanInstallLocalState(bundles, sourceObjects, objectValidity, allowRepair: true)) continue;
            selected = candidate;
            selectedBundles = bundles;
            preservedNewer = true;
            break;
        }

        var previousVersion = installed?.Version.Version ?? _active?.Version.Version ?? string.Empty;
        var sameInstalledRelease = SameRelease(installed, selected);
        var keepPending = false;
        if (sameInstalledRelease)
        {
            try { keepPending = await IsPendingActivationAsync(installed, cancellationToken); }
            catch (YamlException) { }
        }
        RuntimeState? rollback = null;
        if (!sameInstalledRelease && installed is not null &&
            CanInstallLocalState(SelectInstalledLocalBundles(installed.Catalog),
                sourceObjects, objectValidity, allowRepair: false))
            rollback = installed;

        CreateCacheLayout();
        var totalBytes = selectedBundles.Aggregate(0L, static (sum, item) => checked(sum + item.Size));
        long completedBytes = 0, copiedBytes = 0;
        var completed = 0;
        var copied = 0;
        var reused = 0;
        var repaired = 0;
        progress?.Report(new AssetBundleInstallProgress(AssetBundleInstallPhase.Copying,
            0, selectedBundles.Count, 0, totalBytes));
        for (var offset = 0; offset < selectedBundles.Count; offset += LocalInstallBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var count = Math.Min(LocalInstallBatchSize, selectedBundles.Count - offset);
            var copies = new List<(AssetBundleDescriptor Descriptor, bool Repair)>(count);
            // Validate the entire batch before starting any I/O, so an unavailable
            // repair source cannot abandon previously started operations.
            for (var index = 0; index < count; index++)
            {
                var descriptor = selectedBundles[offset + index];
                if (objectValidity[LocalObjectKey(descriptor)]) continue;
                if (!sourceObjects.TryGetValue(descriptor.Sha256, out var available) ||
                    available.Size != descriptor.Size)
                    throw new InvalidDataException($"No bundled repair payload exists for '{descriptor.Name}'.");
            }
            for (var index = 0; index < count; index++)
            {
                var descriptor = selectedBundles[offset + index];
                if (objectValidity[LocalObjectKey(descriptor)]) continue;
                copies.Add((descriptor, _fileSystem.FileExists(GetObjectPath(descriptor))));
            }
            if (_localInstallUsesAsyncIo)
            {
                await CopyLocalNativeBatchAsync(root, copies.Select(static copy => copy.Descriptor).ToArray(),
                    sourceObjects, cancellationToken);
            }
            else
            {
                for (var index = 0; index < copies.Count; index++)
                {
                    var copy = copies[index];
                    await CopyLocalObjectAsync(
                        ResolveInside(root, "bundles/" + sourceObjects[copy.Descriptor.Sha256].FileName),
                        GetObjectPath(copy.Descriptor), copy.Descriptor, cancellationToken);
                }
            }
            foreach (var copy in copies)
            {
                objectValidity[LocalObjectKey(copy.Descriptor)] = true;
                copied++;
                if (copy.Repair) repaired++;
                copiedBytes = checked(copiedBytes + copy.Descriptor.Size);
            }
            reused += count - copies.Count;
            for (var index = 0; index < count; index++)
            {
                var descriptor = selectedBundles[offset + index];
                completedBytes = checked(completedBytes + descriptor.Size);
                progress?.Report(new AssetBundleInstallProgress(AssetBundleInstallPhase.Copying,
                    ++completed, selectedBundles.Count, completedBytes, totalBytes, descriptor.Name));
            }
        }

        progress?.Report(new AssetBundleInstallProgress(AssetBundleInstallPhase.Activating,
            completed, selectedBundles.Count, completedBytes, totalBytes));
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (_loadedBundles.Count != 0)
            throw new InvalidOperationException("AssetBundles were loaded during local release installation.");
        if (!sameInstalledRelease)
            await PersistCatalogAsync(selected, cancellationToken);
        if (rollback is not null)
        {
            await PersistCatalogAsync(rollback, cancellationToken);
            await WritePointerAsync(PreviousPointerPath, rollback.Version, cancellationToken);
        }
        if (!keepPending && _fileSystem.FileExists(PendingPointerPath))
            _fileSystem.DeleteFile(PendingPointerPath);
        // The active pointer is the final disk mutation. Cancellation or a failed copy
        // leaves the old pointer intact; completed content-addressed objects remain reusable.
        if (!sameInstalledRelease)
            await WritePointerAsync(ActivePointerPath, selected.Version, cancellationToken);
        ThrowIfDisposed();
        var refreshResources = !SameRelease(_active, selected);
        _active = selected;
        _initialized = true;
        _hasPendingActivation = keepPending;
        if (refreshResources) BAssetReferenceLoader.Clear();
        progress?.Report(new AssetBundleInstallProgress(AssetBundleInstallPhase.Completed,
            completed, selectedBundles.Count, completedBytes, totalBytes));
        return new AssetBundleInstallResult(!sameInstalledRelease, previousVersion, selected.Version.Version,
            preservedNewer, copied, reused, repaired, copiedBytes);
    }

    private async BValueTask<RuntimeState> ReadLocalReleaseAsync(
        string root, BCancellationToken cancellationToken)
    {
        var versionBytes = await ReadLimitedFileAsync(
            ResolveInside(root, VersionFileName), _options.MaximumCatalogSize, cancellationToken);
        var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
        ValidatePackage(version.PackageName);
        var catalogBytes = await ReadLimitedFileAsync(
            ResolveInside(root, version.CatalogFile), _options.MaximumCatalogSize, cancellationToken);
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "bundled install catalog");
        var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
        ValidateCatalogPair(version, catalog);
        ValidateCatalogLimits(catalog);
        return new RuntimeState(version, catalog, catalogBytes);
    }

    private async BValueTask<RuntimeState?> TryReadInstalledMetadataAsync(
        string pointer, BCancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(pointer)) return null;
        try
        {
            var versionBytes = await ReadLimitedFileAsync(pointer, _options.MaximumCatalogSize, cancellationToken);
            var version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
            ValidatePackage(version.PackageName);
            var catalogBytes = await ReadLimitedFileAsync(
                GetCatalogCachePath(version.CatalogSha256), _options.MaximumCatalogSize, cancellationToken);
            VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "installed catalog");
            var catalog = AssetBundleCatalogSerializer.DeserializeCatalog(catalogBytes);
            ValidateCatalogPair(version, catalog);
            ValidateCatalogLimits(catalog);
            return new RuntimeState(version, catalog, catalogBytes);
        }
        catch (Exception error) when (IsRecoverableContentFailure(error) || error is YamlException)
        {
            return null;
        }
    }

    private IReadOnlyList<AssetBundleDescriptor> SelectInstalledLocalBundles(AssetBundleCatalog catalog)
    {
        if (_requiredBundleNames is null) return catalog.Bundles;
        // Optional remote content that was installed remains part of integrity checks;
        // optional content that was never downloaded does not force a downgrade.
        return ResolveBundleSelection(catalog, GetRequiredBundles(catalog, includeInstalled: false)
            .Select(static item => item.Name)
            .Concat(catalog.Bundles.Where(item => _fileSystem.FileExists(GetObjectPath(item)))
                .Select(static item => item.Name)));
    }

    private static bool CanInstallLocalState(
        IReadOnlyList<AssetBundleDescriptor> bundles,
        IReadOnlyDictionary<string, AssetBundleDescriptor> sourceObjects,
        IReadOnlyDictionary<(string Hash, long Size), bool> objectValidity,
        bool allowRepair)
    {
        foreach (var descriptor in bundles)
        {
            if (objectValidity[LocalObjectKey(descriptor)]) continue;
            if (!allowRepair || !sourceObjects.TryGetValue(descriptor.Sha256, out var source) ||
                source.Size != descriptor.Size) return false;
        }
        return true;
    }

    private async BValueTask<Dictionary<(string Hash, long Size), bool>> PreloadLocalObjectValidityAsync(
        RuntimeState source, RuntimeState? installed, RuntimeState? previous,
        BCancellationToken cancellationToken)
    {
        var distinct = new Dictionary<(string Hash, long Size), AssetBundleDescriptor>();
        foreach (var state in new[] { source, installed, previous })
            if (state is not null)
                foreach (var descriptor in state.Catalog.Bundles)
                    distinct.TryAdd(LocalObjectKey(descriptor), descriptor);
        var descriptors = distinct.Values.ToArray();
        var validity = new Dictionary<(string Hash, long Size), bool>(descriptors.Length);
        var results = await VerifyLocalBatchAsync(null, descriptors, cancellationToken);
        for (var index = 0; index < descriptors.Length; index++)
            validity[LocalObjectKey(descriptors[index])] = results[index];
        return validity;
    }

    private async BValueTask<bool[]> VerifyLocalBatchAsync(
        string? sourceRoot, IReadOnlyList<AssetBundleDescriptor> descriptors,
        BCancellationToken cancellationToken, Action<AssetBundleDescriptor>? completed = null)
    {
        var results = new bool[descriptors.Count];
        if (!_localInstallUsesAsyncIo)
        {
            for (var index = 0; index < descriptors.Count; index++)
            {
                var descriptor = descriptors[index];
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var path = sourceRoot is null ? GetObjectPath(descriptor)
                        : ResolveInside(sourceRoot, "bundles/" + descriptor.FileName);
                    using var stream = OpenLocalInstallInput(path, descriptor);
                    await CopyAndVerifyLocalStreamAsync(stream, null, descriptor, cancellationToken);
                    results[index] = true;
                }
                catch (Exception error) when (sourceRoot is null && IsRecoverableContentFailure(error)) { }
                completed?.Invoke(descriptors[index]);
            }
            return results;
        }

        using var stop = new CancellationTokenSource();
        using var registration = cancellationToken.Register(stop.Cancel);
        for (var offset = 0; offset < descriptors.Count; offset += LocalInstallBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(LocalInstallBatchSize, descriptors.Count - offset);
            var operations = new List<LocalInstallNativeIo>(count);
            ExceptionDispatchInfo? failure = null;
            try
            {
                for (var index = offset; index < offset + count; index++)
                {
                    var descriptor = descriptors[index];
                    var operation = new LocalInstallNativeIo(descriptor, index);
                    operations.Add(operation);
                    try
                    {
                        var path = sourceRoot is null ? GetObjectPath(descriptor)
                            : ResolveInside(sourceRoot, "bundles/" + descriptor.FileName);
                        operation.Input = OpenLocalInstallInput(path, descriptor);
                        operation.Io = CopyAndVerifyLocalStreamWithIoAsync(operation.Input, null,
                            descriptor.Size, descriptor.Sha256, descriptor.Name, stop.Token);
                    }
                    catch (Exception error) when (sourceRoot is null && IsRecoverableContentFailure(error)) { }
                    catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); break; }
                }
                // Continue with the next bounded batch directly, without routing each
                // batch or individual file through another frame's BTask continuation.
                while (operations.Any(static operation => operation.Io is { IsCompleted: false }))
                    await BValueTask.Yield();
                foreach (var operation in operations)
                {
                    if (operation.Io is null) continue;
                    try { operation.Io.GetAwaiter().GetResult(); results[operation.Index] = true; }
                    catch (Exception error) when (sourceRoot is null && IsRecoverableContentFailure(error)) { }
                    catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
                }
                cancellationToken.ThrowIfCancellationRequested();
                failure?.Throw();
            }
            finally
            {
                DisposeLocalNativeStreams(operations)?.Throw();
            }
            for (var index = offset; index < offset + count; index++) completed?.Invoke(descriptors[index]);
        }
        return results;
    }

    private async BValueTask CopyLocalNativeBatchAsync(
        string sourceRoot, IReadOnlyList<AssetBundleDescriptor> descriptors,
        IReadOnlyDictionary<string, AssetBundleDescriptor> sourceObjects,
        BCancellationToken cancellationToken)
    {
        using var stop = new CancellationTokenSource();
        using var registration = cancellationToken.Register(stop.Cancel);
        var operations = new List<LocalInstallNativeIo>(descriptors.Count);
        ExceptionDispatchInfo? failure = null;
        try
        {
            foreach (var descriptor in descriptors)
            {
                operations.Add(new LocalInstallNativeIo(descriptor, operations.Count)
                {
                    Temporary = _pathService.Combine(ObjectsDirectory, $".{descriptor.Sha256}.{Guid.NewGuid():N}.tmp")
                });
            }
            foreach (var operation in operations)
            {
                var descriptor = operation.Descriptor;
                try
                {
                    operation.Input = OpenLocalInstallInput(ResolveInside(sourceRoot,
                        "bundles/" + sourceObjects[descriptor.Sha256].FileName), descriptor);
                    operation.Output = _fileSystem.OpenFile(operation.Temporary!, FileMode.CreateNew,
                        FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    operation.Io = CopyAndVerifyLocalStreamWithIoAsync(operation.Input, operation.Output,
                        descriptor.Size, descriptor.Sha256, descriptor.Name, stop.Token);
                }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); break; }
            }
            while (operations.Any(static operation => operation.Io is { IsCompleted: false }))
                await BValueTask.Yield();
            var copyFailure = ObserveLocalNativeIo(operations);
            failure ??= copyFailure;
            cancellationToken.ThrowIfCancellationRequested();
            failure?.Throw();
            foreach (var operation in operations) _fileSystem.Flush(operation.Output!, flushToDisk: true);
            DisposeLocalNativeStreams(operations)?.Throw();

            foreach (var operation in operations)
            {
                try
                {
                    operation.Input = OpenLocalInstallInput(operation.Temporary!, operation.Descriptor);
                    operation.Io = CopyAndVerifyLocalStreamWithIoAsync(operation.Input, null,
                        operation.Descriptor.Size, operation.Descriptor.Sha256, operation.Descriptor.Name, stop.Token);
                }
                catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); break; }
            }
            while (operations.Any(static operation => operation.Io is { IsCompleted: false }))
                await BValueTask.Yield();
            var verificationFailure = ObserveLocalNativeIo(operations);
            failure ??= verificationFailure;
            cancellationToken.ThrowIfCancellationRequested();
            failure?.Throw();
            DisposeLocalNativeStreams(operations)?.Throw();
            foreach (var operation in operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                _fileSystem.MoveFile(operation.Temporary!, GetObjectPath(operation.Descriptor), overwrite: true);
            }
        }
        finally
        {
            var cleanupFailure = DisposeLocalNativeStreams(operations);
            foreach (var operation in operations)
            {
                try
                {
                    if (operation.Temporary is not null && _fileSystem.FileExists(operation.Temporary))
                        _fileSystem.DeleteFile(operation.Temporary);
                }
                catch (Exception error) { cleanupFailure ??= ExceptionDispatchInfo.Capture(error); }
            }
            cleanupFailure?.Throw();
        }
    }

    private Stream OpenLocalInstallInput(string path, AssetBundleDescriptor descriptor)
    {
        if (!_fileSystem.FileExists(path))
            throw new FileNotFoundException("AssetBundle installation payload was not found.", path);
        if (_fileSystem.GetFileLength(path) != descriptor.Size || descriptor.Size > _options.MaximumBundleSize)
            throw new InvalidDataException($"AssetBundle installation payload '{path}' has an invalid size.");
        return _fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, LocalInstallFileOptions);
    }

    private static ExceptionDispatchInfo? ObserveLocalNativeIo(IEnumerable<LocalInstallNativeIo> operations)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var operation in operations)
        {
            if (operation.Io is null) continue;
            try { operation.Io.GetAwaiter().GetResult(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
        }
        return failure;
    }

    private static ExceptionDispatchInfo? DisposeLocalNativeStreams(IEnumerable<LocalInstallNativeIo> operations)
    {
        ExceptionDispatchInfo? failure = null;
        foreach (var operation in operations)
        {
            try { operation.Output?.Dispose(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            finally { operation.Output = null; }
            try { operation.Input?.Dispose(); }
            catch (Exception error) { failure ??= ExceptionDispatchInfo.Capture(error); }
            finally { operation.Input = null; }
        }
        return failure;
    }

    private sealed class LocalInstallNativeIo(AssetBundleDescriptor descriptor, int index)
    {
        internal AssetBundleDescriptor Descriptor { get; } = descriptor;
        internal int Index { get; } = index;
        internal Stream? Input;
        internal Stream? Output;
        internal Task? Io;
        internal string? Temporary;
    }

    private static (string Hash, long Size) LocalObjectKey(AssetBundleDescriptor descriptor) =>
        (descriptor.Sha256.ToLowerInvariant(), descriptor.Size);

    private static bool IsNewerLocalRelease(string installed, string bundled)
    {
        try
        {
            return AssetBundleVersionLabel.CompareHotResourceVersions(installed, bundled) > 0;
        }
        catch (InvalidDataException)
        {
            // General portable version labels have no ordering contract. Only vN
            // releases have a well-defined upgrade order in the build settings.
            return false;
        }
    }

    private async BValueTask VerifyLocalObjectAsync(
        string path, AssetBundleDescriptor descriptor, BCancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.FileExists(path))
            throw new FileNotFoundException("AssetBundle installation payload was not found.", path);
        if (_fileSystem.GetFileLength(path) != descriptor.Size || descriptor.Size > _options.MaximumBundleSize)
            throw new InvalidDataException($"AssetBundle installation payload '{path}' has an invalid size.");
        using var stream = _fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, LocalInstallFileOptions);
        await CopyAndVerifyLocalStreamAsync(stream, null, descriptor, cancellationToken);
    }

    private async BValueTask CopyLocalObjectAsync(
        string source, string destination, AssetBundleDescriptor descriptor, BCancellationToken cancellationToken)
    {
        var temporary = _pathService.Combine(ObjectsDirectory, $".{descriptor.Sha256}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var input = _fileSystem.OpenFile(source, FileMode.Open, FileAccess.Read, FileShare.Read,
                       64 * 1024, LocalInstallFileOptions))
            using (var output = _fileSystem.OpenFile(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       64 * 1024, LocalInstallFileOptions))
            {
                await CopyAndVerifyLocalStreamAsync(input, output, descriptor, cancellationToken);
                _fileSystem.Flush(output, flushToDisk: true);
            }
            await VerifyLocalObjectAsync(temporary, descriptor, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            _fileSystem.MoveFile(temporary, destination, overwrite: true);
        }
        finally
        {
            if (_fileSystem.FileExists(temporary)) _fileSystem.DeleteFile(temporary);
        }
    }

    private async BValueTask CopyAndVerifyLocalStreamAsync(
        Stream input, Stream? output, AssetBundleDescriptor descriptor, BCancellationToken cancellationToken)
    {
        using var stop = new CancellationTokenSource();
        using var registration = cancellationToken.Register(stop.Cancel);
        if (_localInstallUsesAsyncIo)
        {
            // Only private streams and immutable scalar metadata leave the frame thread.
            // Native asynchronous I/O continues between frames instead of waiting one
            // game frame per read/write chunk. No dedicated thread or Task.Run is required.
            var io = CopyAndVerifyLocalStreamWithIoAsync(input, output, descriptor.Size,
                descriptor.Sha256, descriptor.Name, stop.Token);
            while (!io.IsCompleted) await BValueTask.Yield();
            try { io.GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Browser files are already mounted in memory. Async FileStream
                // operations can still defer every block to another browser turn,
                // adding frame waits to reading, copying and final verification.
                // Bound synchronous memory I/O with the shared elapsed-time slice.
                var length = input.Read(buffer, 0, 64 * 1024);
                cancellationToken.ThrowIfCancellationRequested();
                if (length == 0) break;
                total = checked(total + length);
                if (total > descriptor.Size)
                    throw new InvalidDataException($"AssetBundle '{descriptor.Name}' exceeds its declared size.");
                hash.AppendData(buffer, 0, length);
                if (output is not null)
                {
                    output.Write(buffer, 0, length);
                }
                if (Stopwatch.GetElapsedTime(_localInstallSliceStarted).TotalMilliseconds >=
                    LocalInstallBrowserSliceMilliseconds)
                {
                    await BValueTask.Yield();
                    _localInstallSliceStarted = Stopwatch.GetTimestamp();
                }
            }
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (total != descriptor.Size || !actual.Equals(descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"AssetBundle '{descriptor.Name}' failed installation SHA256 verification.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private FileOptions LocalInstallFileOptions => FileOptions.SequentialScan |
        (_localInstallUsesAsyncIo ? FileOptions.Asynchronous : FileOptions.None);

    private static async Task CopyAndVerifyLocalStreamWithIoAsync(
        Stream input, Stream? output, long expectedSize, string expectedHash, string bundleName,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long total = 0;
            var sliceStarted = Stopwatch.GetTimestamp();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = await input.ReadAsync(buffer.AsMemory(0, 64 * 1024), cancellationToken)
                    .ConfigureAwait(false);
                if (length == 0) break;
                total = checked(total + length);
                if (total > expectedSize)
                    throw new InvalidDataException($"AssetBundle '{bundleName}' exceeds its declared size.");
                hash.AppendData(buffer, 0, length);
                if (output is not null)
                    await output.WriteAsync(buffer.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
                // Memory-backed streams may complete every operation synchronously.
                // Bound initial work on the calling thread even in that case.
                if (Stopwatch.GetElapsedTime(sliceStarted).TotalMilliseconds >= 1)
                {
                    await Task.Yield();
                    sliceStarted = Stopwatch.GetTimestamp();
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (total != expectedSize || !actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"AssetBundle '{bundleName}' failed installation SHA256 verification.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
