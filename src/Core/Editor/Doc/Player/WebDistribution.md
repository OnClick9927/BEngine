# Web Player distribution

The engine's Web publisher produces this layout for every project:

```text
wwwroot/
  index.html
  player.bin
  framework.bin
  assembly/          # only when there are actual external files
  resources/
  StreamingAssets/
```

`player.bin` contains the browser host scripts and the virtual filesystem index.
`framework.bin` contains the SDK's JavaScript, managed/AOT images and runtime data.
`player.bin` uses gzip and `framework.bin` uses Brotli internally. A small WebAssembly
Brotli decoder is embedded inside the player for browsers without Brotli streams.
A static HTTP server can serve both packs as ordinary binary files without
configuring `Content-Encoding`. SDK `.br`/`.gz` sidecars and loose
host scripts are build intermediates and are removed from the distribution.

Project content is copied directly beside `index.html`. Virtual mount paths keep
their original `<product>_data/...` form, so `Application.streamingAssetsPath` and
asset loading code do not change. Content may not use the publisher's reserved
root names, including `index.html`, `player.bin`, `framework.bin`, `_framework`
and the host scripts; conflicting builds fail before content is copied.

Web startup does not require a physical or virtual `assembly` directory. The Web
publisher omits the native Player's `assembly/bengine.directory` placeholder and
does not add a download for it. If a project has actual files under `assembly`,
those files are still published and mounted. Native Players retain their
assembly directory validation.

The two binary filenames are stable. Their URLs carry SHA-256 content versions,
and downloads are checked for integrity before use. The entry page revalidates
`index.html` on ordinary refresh and then selects the corresponding player.
Upload a complete build before making it available to clients. SHA-256 checks
detect an incomplete or mixed deployment but cannot make a partial upload valid.

Deploy the entire `wwwroot` directory through HTTP(S), either at `/` or below a
path such as `/Web/`. Use a real directory share when the server supports it. An
HFS virtual tree assembled from individually added files must be refreshed after
rebuilding, since resource names and directory layouts can change. Opening
`index.html` with a `file:` URL is not supported.

The current player requires WebAssembly, WebGL and `DecompressionStream('gzip')`.
Packing preserves the SDK's final IL/AOT bytes and does not remove runtime
capabilities solely to save download size. The existing Release trimming and AOT
settings continue to apply before packing.

The pack uses Brotli quality 11 with a 16 MiB window. The browser's embedded
decoder supports that window on both HTTP and HTTPS. Web builds also use the
SDK's full ICU dataset instead of downloading all three overlapping locale
shards inside one pack. This retains the runtime's supported cultures; explicit
project ICU settings take precedence.

Web AOT profiles use the `balanced` BCL policy by default. Engine/project native
selections are preserved. Common numeric, string, collection, memory and threading
code remains in the native profile; cold BCL and desktop/decoder dependency code retains its IL and executes
through the hybrid runtime when used. This reduces native code without removing
the XML, reflection, regex or serialization APIs. Projects that use those APIs in
their frame loop can set `BEngineWasmAotBclPolicy=full` through their additional
MSBuild properties to retain the previous BCL AOT coverage. The choice affects
native code size and execution cost, and must be measured against the project's
actual workload.

AOT generic instances are deduplicated by the SDK. After native compilation,
eligible method bodies in ordinary BCL/Host assemblies are stripped by the SDK.
Runtime-loaded script images are restored from their frozen precompiled inputs
before bundling and are checked with SHA-256. They retain their original IL,
metadata and MVID for the AssetBundle/shared assembly contract. Cold methods and
interpreter fallback remain available. A missing, duplicate or corrupted frozen
image fails the build instead of producing a mismatched runtime image.

## PlayerPrefs persistence

Web Players automatically store `PlayerPrefs` in the browser's `localStorage`.
`PlayerPrefs.Save()` synchronously commits the complete preferences, so refreshing
or closing the page does not depend on an unload callback. Other platforms retain
their atomic `PlayerPrefs.yaml` file under `Application.persistentDataPath`.

The Web key is `BEngine.PlayerPrefs.v1:<company>/<product>`, with each name encoded
using `encodeURIComponent`. Browser storage is isolated by origin (scheme, host
and port) and by the project's company/product names. Moving to another origin,
changing these names, clearing site data or ending a private browsing session can
make previous preferences unavailable. Do not put large binary resources in
PlayerPrefs. A blocked store or exhausted browser quota makes `Save()` throw an
`IOException` rather than silently claim success.

This store only contains PlayerPrefs. Other virtual filesystem files still need
an explicit persistence solution. Direct HTTP StreamingAssets loading does not
copy or cache AssetBundles in PlayerPrefs or the browser's persistent filesystem.


## Web loading preparation

`SceneTextureCache.PrepareAsync` can use the optional
`IGraphicsEncodedImagePreparation` device contract. The WebGL backend prepares PNG
with `createImageBitmap`, without a managed pixel copy or a Canvas2D readback. Other
formats and older hosts retain `ImageCodec`. Texture creation still runs through the
engine cooperative scheduler and texture-memory reservation; sampling settings,
resource-generation checks, cancellation and shared residency apply to both paths.
Prepared images are disposable and remain separate from owned GPU textures.

The browser admits at most 32 image jobs, two simultaneous decodes, 64 MiB decoded
reservations and 32 MiB encoded payloads. An individual payload above 16 MiB falls
back to the portable codec. Cancellation releases queued inputs immediately;
in-flight `createImageBitmap` keeps its reservation until its late result closes.
These limits cover engine-owned input and decoded admission, not browser-internal
allocations or GPU memory. Read-only diagnostics report admission, decode/upload
costs, cancellation and current ownership. No runtime progress events are added.

Call `FrameWorkBudget.EnterLoading(token)` around preparation to use the bounded
loading budget; dispose the scope when the operation finishes or waits for scene
activation. It does not change the ordinary gameplay budget. The balanced Web AOT
profile compiles the hot YAML scanner/parser in `YamlDotNet.Core` while retaining
serialization/reflection fallback IL and all original data and language support.
