# Platform I/O and AssetBundle Delivery

Runtime simulation uses Fix64 and one owning thread. Runtime code awaits BTask;
the platform adapter may use operating-system asynchronous I/O, but must expose
progress and completion through INetworkOperation.Poll on the owning thread.

## Platform Services

| Contract | Responsibility | Existing adapter |
| --- | --- | --- |
| FileUtility (IFileSystem SPI) | File contents, streams, enumeration, flush, rename and replacement | PhysicalFileSystem |
| PathUtility (IPathService SPI) | Platform path normalization and composition | SystemPathService |
| PlatformUtility (IPlatformService SPI) | Clock, environment, platform identity and host operations | SystemPlatformService |
| IHttpTransport | HTTP status, headers and streamed response bytes | DotNetHttpTransport; nonblocking SocketHttpTransport fallback |
| IWebSocketTransport | Connect, frames and close handshake | DotNetWebSocketTransport |
| ITcpTransport / IUdpTransport | Stream/datagram network operations | SocketTransportFactory via ISocketTransportFactory |
| IAudioOutput | Interleaved PCM delivery | Audio package OpenAL or silent output |
| IGraphicsDevice | Texture, buffer, shader, render target and draw operations | RHI platform backend |

Player and Editor register AddBEngineDotNetNetworking. HTTPS certificate validation,
HTTP DNS resolution and WebSocket TLS use the .NET platform implementation. The
Networking package TCP/UDP transport accepts IP addresses and localhost; applications needing
other socket name-resolution behavior can register ISocketTransportFactory.
Browser platforms must supply supported transport capabilities; raw sockets must
not be assumed available. Mobile hardware and browser deployment require their own
target validation beyond the Windows checks in this repository.

The Networking package's platform transports isolate Task and cancellation machinery from
the single-thread runtime assemblies. Its continuations release OS resources only.
Callbacks that mutate engine state, stream handlers and BTask completion are invoked
from Poll. Cancellation must be checked before and after Poll because a callback
can abort or dispose the request. Dispose must observe and release pending OS work.

File-system adapters must preserve the contracts used by content installation:
CreateNew rejects an existing file, Flush(true) makes staged data durable where the
platform supports it, and replacement/rename cannot expose partially written active
catalogs. All activation writes use the same file-system service as their reads.
Do not simulate successful writes when a platform cannot persist them.

## Unity-Style Build Maps

```csharp
importer.SetAssetBundleNameAndVariant("shared-art", "");
var map = new[]
{
    new AssetBundleBuild
    {
        assetBundleName = "level-one",
        assetNames = ["Assets/Levels/One.scene.yaml"]
    }
};
// Pass map to BuildPipeline.BuildAssetBundles with the workspace, asset database
// and AssetBundleBuildOptions used by this project's build pipeline.
```

Importer assignments apply to an asset or its closest named ancestor folder.
An asset assignment overrides a folder assignment. A variant is appended to the
bundle name. Player builds put unassigned hot-update content in hotupdate-content.
An explicit AssetBundleBuild array defines the build map directly. Include referenced
assets in the map; references between assigned bundles produce catalog dependencies.
Duplicate ownership, unknown dependencies and cycles are rejected.

The Example/Project folders demonstrate shared-art, scenes and live-content.
Code and package-resource bundles retain the engine's reserved identities.
Compare generated catalog names and dependency edges instead of inferring a split
from the number of output files alone.

## Read Directly from StreamingAssets

Select `Load Mode: StreamingAssets` in AssetBundle Settings, or set
`loadMode: StreamingAssets` and `builtInDirectory: StreamingAssets/AssetBundles`
in `ProjectSettings/AssetBundles.yaml`. Package the standard release under
`Assets/StreamingAssets/AssetBundles/<packageName>` before building the Player.
The normal Player build includes it under `Application.streamingAssetsPath`.
Disable the remote URL and startup update checks for this offline mode.

The Player and its AOT startup use the packaged catalog directly. Initialization
checks the catalog hash and, for file sources, bundle presence/length. Each bundle's SHA-256 is
verified when first opened. Dependencies, encryption and resource lifetimes use
the same loading pipeline. No AB objects, staging files or activation pointers
are written to `Application.persistentDataPath`; game saves may still use it.
In-place updates, rollback and local installation require `PersistentCache` mode.

For a separately managed AB instance:

```csharp
using var manager = new AssetBundleManager(
    AssetBundleRuntimeOptions.FromStreamingAssets("game"));
await manager.InitializeAsync();
using var handle = await manager.LoadBytesAsync("Assets/Config/game.json");
```

Desktop and iOS read packaged files in place. Android mounts StreamingAssets
from the APK without extracting that directory; AB files are stored uncompressed
in the APK and opened as bounded, seekable file segments. Other compressed assets
are expanded into memory on demand.

Web uses the site's `StreamingAssets/` URL, resolved relative to the page (including
subdirectory deployments). These files are not preloaded or mounted into the
virtual filesystem. Initialization fetches only version/catalog metadata; an AB
and its dependencies are fetched when first requested, checked for size/SHA-256,
then opened directly from memory. `HttpNetworkRequest.BypassCache = true` uses
browser fetch `cache: no-store`: no HTTP cache, IndexedDB, sandbox copy or disk
installation. Loaded bundles share their in-memory data while in use; after
unloading, requesting them again fetches them again. Asynchronous loading or
preloading is required before synchronous resource access. Cross-origin sources
must allow CORS. A connection is required whenever an unloaded bundle is requested.

Keep the `AssetBundleHandle` returned by an asynchronous load alive while its UI
or scene may still read bundle payloads synchronously. Dispose it when that scope
ends. `DownloadDependenciesAsync` warms memory without retaining a usage handle;
`UnloadUnused` may evict those preloads. Fetch them asynchronously again before
the next synchronous access.

For a custom HTTP source, configure `UsePersistentCache = false`,
`BuiltInBaseUri = new Uri("https://example.com/StreamingAssets/AssetBundles/game/")`
and `PackageName = "game"`, leaving `BuiltInDirectory` and `RemoteBaseUri` unset.
This is a read-only source, separate from the remote update/install pipeline.

`PersistentCache` remains the default for existing projects and old runtime
metadata. It retains installation, repair, package updates and rollback.

## Selective Installation

Set AssetBundleRuntimeOptions.RequiredBundleNames to required root names before
creating the manager. Null preserves full installation. Explicit roots include
transitive dependencies and mandatory engine code/package bundles. The option is
snapshotted by the manager. Previously installed optional bundles remain eligible
for updates when they exist in the next catalog.

```csharp
var downloads = (IAssetBundleDownloadManager)manager;
var bytes = await downloads.GetDownloadSizeAsync(["level-one"]);
await downloads.DownloadDependenciesAsync(["level-one"], progress, token);
```

Use CheckForUpdatesAsync and ApplyUpdateAsync for a catalog version change.
DownloadDependenciesAsync installs an optional group against the already active
catalog; it does not select a new version. Downloads are deduplicated through the
catalog dependency closure and content-addressed object cache. An unknown name is
an error, not an empty successful install.

Verification hashes streams with a configurable per-frame byte budget. A bundle is
verified when opened without hashing it twice on the same load path. Cancellation
of one caller must not discard a healthy shared load result. Failed verification
must leave the previous active catalog usable. Restart, offline cache and rollback
are covered by AssetBundleHotUpdate regression tests.

## Pixel Artwork

Set Texture.filterMode to Point and add PixelPerfectCamera to the output Camera2D.
Match assetsPPU to the authored pixel density; set the reference dimensions to the
game's logical viewport. The camera selects an integer zoom and snaps camera/sprite
centers without changing simulation transforms. A differently shaped viewport shows
more of the world; it does not introduce letterboxing. Rotation and nonuniform scale
can still change pixel coverage, and Bloom intentionally spreads neighboring pixels.

Sprite retains its Texture so runtime artwork survives garbage collection. Texture.Apply
and sampling changes invalidate GPU cache entries. Vulkan supports offscreen targets,
format-specific pipelines and deferred resource retirement. The GPU regressions compare
source colors, force GC, resize targets and exercise post-process enable/disable.
