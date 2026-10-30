using System.Net;
using System.Security.Cryptography;
using BEngine.Networking;

namespace BEngine.AssetBundles;

public sealed partial class AssetBundleManager
{
    private bool IsHttpBundleLoaded(AssetBundleDescriptor descriptor) =>
        _loadedBundles.TryGetValue(descriptor.Sha256, out var task) && task.IsCompletedSuccessfully;

    private async BValueTask<AssetBundleUpdateResult> PreloadHttpBundlesAsync(RuntimeState state,
        IEnumerable<string> names, IProgress<AssetBundleUpdateProgress>? progress, BCancellationToken cancellationToken)
    {
        var selected = ResolveBundleSelection(state.Catalog, names);
        var missing = selected.Where(bundle => !IsHttpBundleLoaded(bundle)).ToArray();
        var total = missing.Sum(static bundle => bundle.Size);
        var count = 0;
        long bytes = 0;
        progress?.Report(new(AssetBundleUpdatePhase.Downloading, 0, missing.Length, 0, total));
        foreach (var descriptor in selected)
        {
            foreach (var indexed in state.Index.GetBundleClosure(descriptor.Name))
            {
                var needsTransfer = !IsHttpBundleLoaded(indexed.Descriptor);
                var bundle = await AcquireBundleAsync(indexed.Descriptor, indexed.Assets, cancellationToken);
                bundle.Release();
                if (!needsTransfer) continue;
                count++;
                bytes += indexed.Descriptor.Size;
                progress?.Report(new(AssetBundleUpdatePhase.Downloading, count, missing.Length,
                    bytes, total, indexed.Descriptor.Name));
            }
        }
        progress?.Report(new(AssetBundleUpdatePhase.Completed, count, missing.Length, bytes, total));
        return new(false, state.Version.Version, state.Version.Version, count, bytes);
    }

    private async BValueTask<RuntimeState> LoadHttpBuiltInStateAsync(BCancellationToken cancellationToken, AssetBundleLoadTracker? progress = null)
    {
        var root = _options.BuiltInBaseUri!;
        byte[] versionBytes;
        var pointer = false;
        try
        {
            versionBytes = await FetchBytesWithRetryAsync(new Uri(root, VersionFileName),
                AssetBundleHttpResourceKind.VersionMetadata, _options.MaximumCatalogSize, cancellationToken, progress);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            pointer = true;
            versionBytes = await FetchBytesWithRetryAsync(new Uri(root, LatestFileName),
                AssetBundleHttpResourceKind.VersionPointer, _options.MaximumCatalogSize, cancellationToken, progress);
        }
        var releaseRoot = root;
        if (!TryDeserializeVersion(versionBytes, out var version))
        {
            if (!pointer) throw new InvalidDataException("The built-in HTTP version metadata is invalid.");
            var latest = AssetBundleCatalogSerializer.DeserializeLatestPointer(versionBytes);
            releaseRoot = HttpContentUri(root, NormalizeVersionSegment(latest.Version) + "/");
            versionBytes = await FetchBytesWithRetryAsync(new Uri(releaseRoot, VersionFileName),
                AssetBundleHttpResourceKind.VersionMetadata, _options.MaximumCatalogSize, cancellationToken, progress);
            version = AssetBundleCatalogSerializer.DeserializeVersion(versionBytes);
            if (!version.Version.Equals(latest.Version, StringComparison.Ordinal))
                throw new InvalidDataException("The built-in HTTP pointer and version metadata do not match.");
        }
        ValidatePackage(version.PackageName);
        _ = NormalizeVersionSegment(version.Version);
        progress?.Report(new(AssetBundleLoadKind.Version, AssetBundleLoadPhase.Completed, VersionFileName, versionBytes.Length, versionBytes.Length));
        byte[] catalogBytes;
        try
        {
            catalogBytes = await FetchBytesWithRetryAsync(HttpContentUri(releaseRoot, version.CatalogFile),
                AssetBundleHttpResourceKind.Catalog, _options.MaximumCatalogSize, cancellationToken, progress);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound && releaseRoot == root)
        {
            releaseRoot = HttpContentUri(root, version.Version + "/");
            catalogBytes = await FetchBytesWithRetryAsync(HttpContentUri(releaseRoot, version.CatalogFile),
                AssetBundleHttpResourceKind.Catalog, _options.MaximumCatalogSize, cancellationToken, progress);
        }
        VerifyPayload(catalogBytes, version.CatalogSize, version.CatalogSha256, "built-in HTTP catalog");
        progress?.Report(new(AssetBundleLoadKind.Catalog, AssetBundleLoadPhase.Opening, version.CatalogFile, catalogBytes.Length, version.CatalogSize));
        await BTask.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = DeserializeCatalogPair(version, catalogBytes, out var indexSeeds);
        ThrowIfDisposed();
        _builtInBundleUris.Clear();
        foreach (var descriptor in catalog.Bundles)
            _builtInBundleUris[descriptor.Sha256] = HttpContentUri(releaseRoot, "bundles/" + descriptor.FileName);
        progress?.Report(new(AssetBundleLoadKind.Catalog, AssetBundleLoadPhase.Completed, version.CatalogFile, catalogBytes.Length, version.CatalogSize));
        // Only the catalog is read at initialization. Bundle existence and integrity are checked on first use.
        return new RuntimeState(version, catalog, catalogBytes, indexSeeds);
    }

    private static Uri HttpContentUri(Uri root, string relativePath)
    {
        BEngine.Serialization.AssetDataValidation.ValidateOptionalRelativePath(relativePath.TrimEnd('/'), nameof(relativePath));
        var escaped = string.Join('/', relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));
        return new Uri(root, escaped);
    }

    private async BValueTask<Stream> ReadHttpBundleAsync(
        Uri uri, AssetBundleDescriptor descriptor, BCancellationToken cancellationToken, AssetBundleLoadTracker? progress = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var handler = new MemoryBundleDownloadHandler(descriptor, _options.MaximumBundleSize);
                await SendHttpStreamingAsync(uri, AssetBundleHttpResourceKind.Bundle,
                    attempt + 1, handler, cancellationToken, descriptor.Name, descriptor.Size, progress);
                return await handler.TakeVerifiedStreamAsync(cancellationToken);
            }
            catch (Exception exception) when (!_disposed && IsRetryable(exception, cancellationToken) && attempt < _options.MaxRetries)
            {
                progress?.Report(new(AssetBundleLoadKind.Bundle, AssetBundleLoadPhase.Retrying, descriptor.Name, 0, descriptor.Size, attempt + 2));
                await BValueTask.Delay(RetryDelay(attempt), cancellationToken);
            }
        }
    }

    private sealed class MemoryBundleDownloadHandler : IHttpDownloadHandler, IDisposable
    {
        private readonly AssetBundleDescriptor _descriptor;
        private readonly long _maximumSize;
        private readonly bool _useNativeHash;
        private readonly IncrementalHash? _hash;
        private readonly IBrowserAssetBundleHashBridge? _nativeBridge;
        private byte[]? _bytes;
        private int _received;
        private bool _nativeVerified;

        internal MemoryBundleDownloadHandler(AssetBundleDescriptor descriptor, long maximumSize)
            : this(descriptor, maximumSize, null) { }

        internal MemoryBundleDownloadHandler(AssetBundleDescriptor descriptor, long maximumSize, IBrowserAssetBundleHashBridge? nativeBridge)
        {
            _descriptor = descriptor; _maximumSize = maximumSize;
            _nativeBridge = nativeBridge;
            _useNativeHash = nativeBridge is null ? BrowserAssetBundleHash.IsSupported(descriptor.Size)
                : BrowserAssetBundleHash.IsSupported(descriptor.Size, nativeBridge);
            _hash = _useNativeHash ? null : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        }

        public void ReceiveResponse(HttpTransportResponse response)
        {
            if (!IsSuccess(response.StatusCode)) return;
            if (_descriptor.Size > _maximumSize || _descriptor.Size > Array.MaxLength || _descriptor.Size < 0)
                throw new InvalidDataException($"HTTP bundle '{_descriptor.Name}' exceeds its memory size limit.");
            if (TryGetContentLength(response, out var length) && length != _descriptor.Size)
                throw new InvalidDataException($"HTTP bundle '{_descriptor.Name}' has an unexpected Content-Length.");
            _bytes = new byte[(int)_descriptor.Size];
        }

        public void ReceiveData(ReadOnlyMemory<byte> data)
        {
            if (_bytes is null) return;
            if (data.Length > _bytes.Length - _received)
                throw new InvalidDataException($"HTTP bundle '{_descriptor.Name}' exceeds its declared size.");
            using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.HttpCopy, data.Length))
                data.Span.CopyTo(_bytes.AsSpan(_received));
            if (_hash is not null)
                using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.HttpSha, data.Length))
                    _hash.AppendData(data.Span);
            _received += data.Length;
        }

        internal async BValueTask<Stream> TakeVerifiedStreamAsync(BCancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_useNativeHash)
            {
                if (_bytes is null || _received != _descriptor.Size)
                    throw new InvalidDataException($"HTTP bundle '{_descriptor.Name}' failed size verification.");
                using (AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.HttpSha, _bytes.Length))
                {
                    if (_nativeBridge is null)
                        await BrowserAssetBundleHash.VerifyAsync(_bytes, _descriptor.Sha256, cancellationToken);
                    else await BrowserAssetBundleHash.VerifyAsync(_bytes, _descriptor.Sha256, cancellationToken, _nativeBridge);
                }
                _nativeVerified = true;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return TakeVerifiedStream();
        }

        internal Stream TakeVerifiedStream()
        {
            using var measurement = AssetBundlePerformanceDiagnostics.Measure(AssetBundlePerformanceDiagnostics.Phase.HttpSha);
            if (_bytes is null || _received != _descriptor.Size ||
                (_useNativeHash ? !_nativeVerified :
                    !Convert.ToHexString(_hash!.GetHashAndReset()).Equals(_descriptor.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"HTTP bundle '{_descriptor.Name}' failed size/SHA-256 verification.");
            var stream = new MemoryStream(_bytes, writable: false);
            _bytes = null;
            return stream;
        }

        public void Dispose()
        {
            _bytes = null;
            _hash?.Dispose();
        }
    }
}
