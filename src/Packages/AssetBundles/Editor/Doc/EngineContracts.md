# AssetBundles implementation contracts

## Preserve AssetBundle indexing and runtime updates

- Treat runtime catalogs as immutable snapshots. `AssetBundleCatalogIndex` clones a publicly supplied catalog and freezes address, bundle, per-bundle asset, and precomputed dependency-closure indexes. Use `ContainsAddress` and `GetDependencyClosure` for public inspection.
- Construct one index when each manager runtime state is created. Keep `LoadBytesAsync` and `LoadTextAsync` on dictionary lookups; do not reintroduce per-load `FirstOrDefault`, `ToDictionary`, asset `Where` scans, or dependency graph traversal.
- Support concurrent `InitializeAsync` and concurrent loads on one manager. Share one loaded-bundle task per content hash, protect cache creation/removal, reference counts, ZIP access, asset cache, and disposal against races, and release every `AssetBundleHandle<T>`.
- Serialize state-changing `InitializeAsync`, `ApplyUpdateAsync`, `RollbackAsync`, and `CleanupAsync` operations. Publish a fully constructed state atomically. A load captures one state snapshot and must remain valid while a newer state is published; concurrent application of the same plan performs one activation.
- Keep remote version/catalog checks, maximum sizes, SHA-256 verification, content-addressed object names, path traversal rejection, dependency validation, and active/previous rollback pointers.
- The package owns expected hashes and verification policy, not a hashing backend. `IPlatformSha256Service` supplies incremental digests and optional asynchronous leases. DotNet uses the system SHA-256 provider; Web supplies WebCrypto and a bounded streaming fallback. A declined native admission can yield and retry; an accepted failure must propagate. Cancellation releases the lease once, while the browser keeps unabortable work charged until settlement. Catalog fingerprints remain usable before host initialization through the framework SHA fallback.
- Stream bundle downloads through `HttpNetworkRequest.SendAsync(responseHandler, token)` with response-headers-first completion. Write into staging, incrementally hash and enforce declared/maximum sizes, flush, then move only a verified file into the object cache. Do not buffer a complete bundle in memory.
- Atomically persist catalogs and active/previous pointers through temporary files. On download or verification failure, remove `.part` files and leave the previous active state intact. Rollback switches verified pointers and must not download.
- Runtime is read-only: never save back to `Assets`, mutate a manager by editing `ActiveCatalog`, edit the index, or modify installed bundle content. Publish new content only through build, check, download, verify, and atomic apply.

```csharp
await using var bundles = new AssetBundleManager(new AssetBundleRuntimeOptions
{
    PackageName = "com.example.game",
    CacheDirectory = cachePath,
    BuiltInDirectory = builtInPath,
    RemoteBaseUri = new Uri("https://cdn.example.test/content/"),
    RequireHttps = true
});
await bundles.InitializeAsync(cancellationToken);
var plan = await bundles.CheckForUpdatesAsync(cancellationToken);
if (plan.HasUpdates)
    await bundles.ApplyUpdateAsync(plan, cancellationToken: cancellationToken);
await using var text =
    await bundles.LoadTextAsync("Assets/Data/config.json", cancellationToken);
```
