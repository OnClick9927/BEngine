# SHA-256 host ownership

`IPlatformSha256Service` and its incremental/operation contracts are neutral host
capabilities. They contain no resource identifiers, catalogs, expected hashes,
download policy, or scheduler dependency.

DotNet owns the system cryptography provider. Web owns the six JavaScript imports,
native operation leases and the bounded managed fallback. The JavaScript host
retains its existing 64 MiB aggregate input and 32-job limits; released,
unabortable digests remain charged until settlement. Borrowed WASM input is copied
synchronously before returning and is never modified.

AssetBundles retains resource admission, catalog expectations, size/EOF checks,
case-insensitive digest comparison, cancellation and cooperative BTask polling.
The public package fingerprint format, content identifiers and serialized formats
are unchanged. No concrete hashing type is referenced from Core or the package.

The subsequent managed-session migration moved deployment manifests and generic
runtime strategies to Core Player. AssetBundles no longer directly references
DotNet. Its content bootstrap and runtime metadata policies still require semantic
review; neither migration completes the entire 44-package audit.

Regression entry points are `Tests/HashPlatformOwnership/Probe` and `Web`.
The probe runs against the previous and current resource assemblies and compares
fingerprints, HTTP bodies and cooperative ZIP payloads. The browser probe runs
actual WebAssembly/WebCrypto and checks unavailable/failed native providers,
admission pressure, stable copied input, cancellation and late settlement.
