# Editor state mechanisms and workflow policy

Core Editor owns generic in-memory state mechanisms:

- `EditorAssetInfo` is an immutable asset query value: identity, source/artifact paths, asset type, directory and subasset identity. It contains no importer, database service or package policy.
- `EditorValueCloner` copies managed graphs. Buffer copies preserve private/readonly state, aliases and cycles, borrow live engine objects and never call unrelated getters. Mapped serialized copies can rebind CLR layouts and map engine references. These two existing semantics remain distinct.
- `EditorObjectSnapshot` captures and restores in-memory values, component enabled state, GameObject state, Transform pose/order and opaque prefab provenance. Live engine references retain identity, including transient Play Mode objects. `CopyValuesTo` preserves the destination identity and provenance. Public member queries return an owned list, not the component reflection cache.
- `EditorStateMemoryEstimate` estimates retained snapshot payload for memory budgets, deduplicates shared references and never invokes user getters. It is an estimate rather than a heap profiler.

EditorServices owns the editing workflow: preview-original-state coordination, Undo/Redo history, budget eviction, dirty marking and Inspector buffer apply. Its internal `ObjectState` adapter derives from the Core snapshot, adding those policies without another wrapper object. Core never references EditorServices or a concrete package identity.

This snapshot is deliberately separate from Runtime `ObjectStateSnapshot`, which represents persistent serialized data. An editor Undo operation must retain live engine identities and transient values rather than reload persistent resource references.

## Migration

- Replace internal `EditorAssetRecord` use with public `EditorAssetInfo`. All repository host and asset-query consumers migrate together.
- `EditorValueCloner` keeps its namespace and method signatures but moves from EditorServices to Core Editor. Recompile editor extensions; no type forwarder is added.
- The snapshot and memory-estimate classes contain no Player code and add no dependency to Core Runtime, Hosts or Platform.
- No scene/Prefab wire data, resource GUID, project settings, runtime load policy or package manifest is changed.
- Prefab reconciliation and structural object restoration still have other private accesses to audit. These contracts do not imply the whole package boundary audit is finished.

## Verification

`Test-EditorStateContracts.ps1` builds independent consumers without friend grants. Core-only checks load no graphics, GUI, workbench or EditorServices packages. Workflow checks cover Undo/Redo, managed aliases/cycles, Inspector buffer apply, preview policy, structural component identity and history budgets.

`Tests/EditorStateContracts/Benchmark` uses the same public Undo operations on before/after binaries. Pair measurements using a declared schedule and retain every sample. Report allocation/CPU differences honestly; a boundary change is not automatically a speed improvement.
