# Resource storage and Core object contracts

Storage providers own addresses, payload delivery, leases and caches. They do not
interpret the engine's text, managed-asset or scene document schemas. The source
asset package continues to own editable documents, import metadata and Prefab
inheritance/merge policy. This change does not move those implementations into
the resource storage package.

`RuntimeAssetDecoding` exposes registered decoder discovery, synchronous decoding
and the existing asynchronous preparation/commit path. `Decode` retains custom
decoder precedence, then handles fundamental Core asset formats. Text uses the
same UTF BOM and malformed-sequence replacement rules. Decoded objects belong to
the caller; storage identity is applied separately through `RuntimeAssetReferences`.
The provider must invoke decoding and binding on the engine owner thread.

`RuntimeAssetReferences.Bind` validates identity before changing an object, keeps
sub-asset object GUIDs distinct from owner references, and does not invent physical
paths for remote/embedded resources. `BindScenePath` supplies the scene's logical
path without exposing a general property setter. Optional relative path validation
and the existing `BAsset.ClearLoadedAssets()` keep the underlying cache private.

`SceneAssetLoading` reads either existing YAML or compiled scene bytes. Its
`SceneLoadData` is an opaque value handle: no mutable DTO, backend implementation
or document collection becomes public. Default handles are invalid. Restoration
retains GUIDs, owner-thread scheduling, progress, cancellation and teardown. The
optional source document codec is still used by `LoadSource`.

`RuntimeObjectDiagnostics` exposes read-only domain counts and transient-state
observations, without registry access or writable flags. `Profiler.RegisterRecorder`
now returns its owned `RecorderRegistration`. The recorder publishes completed
samples through that registration; disposing it rejects later submissions and
preserves other live registrations. The registration constructor and internal
recorder chain remain private to Core. Existing binaries calling RegisterRecorder
must be recompiled because its CLR return type changed.

`RuntimeConfiguration.Apply` applies company/product identity alongside the other
validated runtime settings. Callers no longer duplicate writes to private global
application properties. `PlayerHostServices.ConfigureDataDirectory` sets a normalized
host-selected data directory during startup and performs no filesystem writes.

## Verification

`Test-AssetStorageBoundary.ps1 -EngineBinaries <closure> -OutputDirectory <Temp/...>`
builds a consumer without friend access. It covers text encodings, decoder
registration/removal, asset/sub-asset identity, rejected-input atomicity, YAML and
compiled scene compatibility, asynchronous progress/cancellation, host settings,
recorder lifetimes and object counts. The scene/resource checks initialize no GPU,
window or UI package. Its warmed publication microbenchmark measures only the
registration/sink path with a nonallocating subscriber; it is not a zero-allocation
claim for a complete diagnostics session.

The resource-storage and diagnostics Runtime assemblies also compile with
`BEnginePackageInternalAccess=false`. This is a per-boundary result, not proof
that every package is independent of Core internals: source Prefab/document and
editor adapters still require their own audit.
