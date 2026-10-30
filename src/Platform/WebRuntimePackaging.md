# Web runtime packaging

`framework.bin` contains the SDK's runtime files in a shared Brotli stream.
Version 4 can reuse PE images **already present** in HTTP StreamingAssets
AssetBundles. It never adds runtime files to AssetBundles or changes their bytes.

The build checks each candidate's SHA-256, ZIP records, decompressed size and CRC.
Only stored/deflate archives up to 16 MiB qualify, with a total archive budget of
32 MiB. Unsupported, encrypted, ambiguous or unverifiable sources remain inline.
The build generates a bounded copy/literal/zero delta, applies it independently,
and compares the result with the SDK's published WebCIL file before accepting it.

The browser checks the entire downloaded archive, source PE, delta and restored
WebCIL against their recorded SHA-256 values. SDK boot names, metadata, IL bytes
and boot integrity hashes remain unchanged. The existing integrity fallback
supports LAN HTTP deployments without Web Crypto.

Archive downloads are shared during startup. An in-memory, one-use handoff lets
the game's first ordinary GET consume that download without fetching it again.
Each response chunk owns at most 64 KiB; the existing runtime HTTP receive
budgets remain unchanged. Conditional/range/authenticated or already-cancelled
requests use the normal HTTP path. Unused handoffs expire after 60 seconds;
startup failures discard them immediately. Nothing is written to a sandbox,
IndexedDB, localStorage or the browser Cache API by this mechanism.

After changing packaged assemblies, publish the complete player together with
its matching immutable StreamingAssets files. Versioned player/framework URLs
and integrity checks detect mixed deployments.

Web builds can select `webFontBackend: FreeType` in Player Settings to replace
the Skia font rasterizer with the compact FreeType boundary. HarfBuzz shaping,
layout, fallback and the existing text caches remain enabled. Skia remains the
default, including on other platforms. The compact mode supports outline,
bitmap and COLRv0 fonts; admitted COLRv1/SVG fonts or surviving project code
that references Skia cause a build error directing the developer to select
Skia. The native source, pinned dependency inputs and notices are documented
in `Hosts/Web/Native/README.md`.

The selected backend also determines runtime module admission. FreeType builds
omit the two unregistered original Skia snapshots from both content partitions
and remove their dependency edges. Full canonical builds validate surviving
type references after linking, so unused Skia code may be removed while a live
dependency is rejected. A stale AssemblyRef row alone does not require Skia.

Measure the complete distribution when comparing build profiles. If excluding
`resources/aot.bresources` and `StreamingAssets/**`, native AOT code embedded in
`framework.bin` still counts. Neither source assembly deduplication nor a font
profile guarantees a particular total size.

A project that previously kept the whole Core assembly for a dynamically loaded
game can explicitly limit that legacy root to builds without a complete static
Web Host contract:

```xml
<assembly fullname="BEngine" preserve="all"
          feature="BEngine.Build.StaticWebHost"
          featurevalue="false" featuredefault="true" />
```

The engine enables that feature only in the final SDK Host link for
self-contained, Full-trimmed, canonical Web AOT builds, after generating the
precise Host descriptor and validating the frozen script graph. The earlier
partial canonical link retains the legacy root: skipped Host assemblies must
still keep their interface implementations in package and game modules.
The final SDK linker's arguments append
the feature to the frozen copy policy, including nested publishes. Desktop,
partial, interpreted and unqualified builds retain the legacy root; unconditional
project roots are always respected. Asset/reflection roots and frozen PE identity
checks remain enabled. This opt-in permits removal of unused Core APIs; future
downloaded code must match the shipped Host contract or use additional explicit
roots. It does not promise that every public Core API survives trimming.
