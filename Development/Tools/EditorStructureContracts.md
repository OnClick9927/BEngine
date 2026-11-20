# Editor structure snapshots and workflow boundaries

Core Editor provides neutral, in-memory editing mechanisms:

`EditorObjectStructureSnapshot.CanRemove` queries the current structural removal
rules without capturing a snapshot or mutating any object. Null, unattached
components, destroyed objects, Transform and non-structural assets return false.
A live component on a detached GameObject can still be edited. Required-component
checks use Core's existing removal rules and reflect current membership, including
equivalent remaining components. The answer is not a reservation: query again after
editing the graph. Menus, warnings, Undo and dirty-state policy stay in the caller.
Warmed repeated-query allocation is covered by the independent Core consumer.

- `EditorObjectStructureSnapshot` captures scene membership, parent/sibling relationships and a component's position. `TryRemove(out affectedScene)` respects component requirements. `Restore` reuses live objects/components, re-registers them and invalidates Core queries through existing scene/component operations. It does not copy values, invent a scene for detached objects or restore into a disposed scene.
- `EditorHierarchySnapshot` retains structure and serialized in-memory values for a hierarchy. Restoration handles added/deleted nodes, dependency-safe component ordering, cross-scene moves, original parents and sibling order. `CaptureInverse` tracks objects introduced since capture so repeated restoration retains identities and removes absent objects correctly. `GetTargets` returns a detached array containing the targets captured at construction.
- A protected constructor accepts a value-capture function. A workflow may supply preview-aware `EditorObjectSnapshot` subclasses; Core does not know the workflow's assembly or policy. This callback is used while capturing, rather than stored or scanned every frame. Derived implementations can reuse their captured target array without an extra wrapper allocation.

EditorServices keeps Undo/Redo history, preview coordination, dirty marking, scene-change policy and hierarchy notifications. `StructuralObjectState` and `FullHierarchyState` are small adapters deriving from Core snapshots. The existing history budget behavior is unchanged. The workbench and Inspector query serializable members through `EditorObjectSnapshot` directly, removing a private EditorServices wrapper dependency.

These are editor mechanisms, not runtime resurrection APIs. Core Runtime, Hosts and Platform gain no package dependency. No serialized DTO becomes public; there are no JSON round trips or changes to scene, Prefab, GUID, project settings or Player protocols. Recompile editor extensions after the assembly ownership change.

## Verification

`Test-EditorStructureContracts.ps1` runs independent consumers without friend grants. The Core probe runs without EditorServices, rendering, IMGUI or workbench assemblies. The workflow probe verifies structural Undo/Redo, preview-original values, live identities, cross-scene edits, required components and notification/dirty policy. `-CoreOnly` also supports testing Core built with generated package friendship disabled.

`Tests/StructureSnapshotContracts/Benchmark` uses only existing public Undo APIs and can be compiled against both before and after binaries. It measures the same 500-round mix of full-hierarchy capture/Undo/Redo and component deletion/restoration, with a fixed eight-child hierarchy. Keep all paired samples and report allocation, timing and distribution sizes; an ownership correction is not automatically a performance improvement.

Prefab override reconciliation and asset-database private access remain separate audit items. These contracts do not complete the overall responsibility review.
