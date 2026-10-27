namespace BEngine.HotUpdate;

/// <summary>Resource bytes and the ownership which keeps them valid until the consumer has copied them.</summary>
public sealed class ManagedCodeResource : IDisposable
{
    private IDisposable? _lease;
    public ReadOnlyMemory<byte> bytes { get; }

    public ManagedCodeResource(ReadOnlyMemory<byte> bytes, IDisposable? lease = null)
    { this.bytes = bytes; _lease = lease; }

    public void Dispose()
    {
        var lease = _lease; _lease = null;
        lease?.Dispose();
    }
}

/// <summary>
/// Reads a release using bounded I/O concurrency. This only prepares images: module dependency
/// ordering, assembly validation and activation remain owned by HotUpdateActivationGate.
/// </summary>
public sealed class ManagedCodeReleaseLoadOperation : AsyncOperation, IDisposable
{
    private AsyncLoadBatch<ManagedCodeResource>? _batch;
    private BCancellationRegistration _externalCancellation;
    private bool _disposed;
    public ManagedCodeRelease? release { get; private set; }
    public int resourceCount => _batch?.count ?? 0;
    public int completedResourceCount => _batch?.completedCount ?? 0;

    public ManagedCodeReleaseLoadOperation(Func<string, AsyncLoadContext, BValueTask<ManagedCodeResource>> loader,
        string manifestAddress = ManagedCodeReleaseManifest.DefaultAddress, int maximumConcurrency = 4,
        Func<string, bool>? resourceExists = null, BCancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestAddress);
        if (maximumConcurrency is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        _externalCancellation = cancellationToken.Register(() => Cancel());
        Start(async token =>
        {
            try
            {
                using var loading = FrameWorkBudget.EnterLoading(token);
                token.ThrowIfCancellationRequested();
                using var manifestBytes = await loader(manifestAddress,
                    new AsyncLoadContext(token, value => SetProgress(.05f * value)) { priority = priority });
                token.ThrowIfCancellationRequested();
                if (manifestBytes.bytes.Length > 2 * 1024 * 1024)
                    throw new InvalidDataException("The managed-code release manifest exceeds 2 MiB.");
                var manifest = ManagedCodeReleaseManifestSerializer.Deserialize(manifestBytes.bytes.Span);
                if (manifest.Modules.Count > 4096)
                    throw new InvalidDataException("The managed-code release contains too many modules.");
                _ = ManagedCodeGraph.Order(manifest.Modules, static item => item.Name, static item => item.Dependencies);
                var addresses = new List<string>();
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                void Add(string address)
                {
                    if (index.TryAdd(address, addresses.Count)) addresses.Add(address);
                }
                foreach (var module in manifest.Modules)
                {
                    Add(module.AssemblyAddress);
                    if (!string.IsNullOrWhiteSpace(module.SymbolsAddress) &&
                        (resourceExists is null || resourceExists(module.SymbolsAddress))) Add(module.SymbolsAddress);
                }
                _batch = new AsyncLoadBatch<ManagedCodeResource>(addresses.Count,
                    (item, context) => loader(addresses[item], context), maximumConcurrency,
                    static resource => resource.Dispose(), token) { priority = priority };
                while (!_batch.isDone)
                {
                    SetProgress(.05f + .9f * _batch.progress);
                    await BTask.Yield();
                }
                await _batch;
                var modules = new ManagedCodeModule[manifest.Modules.Count];
                for (var i = 0; i < modules.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var module = manifest.Modules[i];
                    var assembly = _batch.GetResult(index[module.AssemblyAddress]).bytes;
                    var symbols = !string.IsNullOrWhiteSpace(module.SymbolsAddress) && index.TryGetValue(module.SymbolsAddress, out var symbolIndex)
                        ? _batch.GetResult(symbolIndex).bytes : ReadOnlyMemory<byte>.Empty;
                    if (assembly.Length > 128 * 1024 * 1024 || symbols.Length > 128 * 1024 * 1024)
                        throw new InvalidDataException($"Managed-code module '{module.Name}' exceeds the image size limit.");
                    // ManagedCodeModule takes its own copy before the batch releases the source lease.
                    modules[i] = new ManagedCodeModule(module.Name, module.BuildId,
                        assembly.Span, symbols.Span, module.Dependencies);
                    SetProgress(.95f + .049f * (i + 1) / modules.Length);
                }
                release = new ManagedCodeRelease(manifest.ReleaseId, modules, manifest.ContractVersion);
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                _externalCancellation.Dispose();
                _batch?.Dispose();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
        _batch?.Dispose();
        // The immutable release owns its image copies and remains valid for an active domain.
    }
}
