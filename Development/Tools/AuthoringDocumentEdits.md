# Authoring document edit boundary

Core owns the serialized hierarchy and scene document formats. Authoring consumers
edit detached `SceneDocumentSnapshot`, `PrefabDocumentSnapshot`, and
`SerializedHierarchy` values through public contracts instead of accessing storage DTOs.

`RewriteMetadata` maps tag text and layer integers without defining their meaning.
`RewriteScalars` visits legacy fields and top-level graph scalars of Transform and
components. It passes the member name, optional serialized type name, and text to
the caller. It does not descend into objects, arrays, or references. A member changed
in both representations counts once per component, ignoring member-name case.
The working snapshot is mutable: clone it before editing when a failed callback
must leave the caller's original unchanged.

Core Editor provides `PrefabBaselineChange.Prepare` for inherited snapshots of the
connected instances supplied by the caller. It deduplicates references, copies
callback data before publication, and rejects stale apply/rollback or a replacement
made during preparation. It does not choose open scenes, write assets, or perform
project-wide transactions. A callback exception publishes no changes owned by that
preparation; side effects performed by the callback remain the caller's responsibility.

EditorWorkbench owns Tags/Layers draft validation and remapping policy, including
the meaning of `sortingLayer`, `cullingMask`, and `*LayerMask`, typed UInt64 filtering,
open scene/stage selection, atomic source writes, settings persistence, rollback,
Undo invalidation, reimport, and repaint. It composes the public document and baseline
mechanisms without referencing Core's private scene, prefab, component, or graph DTOs.

`Test-AuthoringDocumentEdits.ps1` validates an independent consumer, detached scene
documents, baseline isolation and conflict handling, actual old/new Workbench binary
parity for scenes/prefabs/inherited references, and filesystem failure rollback. Test
reflection sets up fixtures and invokes the internal Workbench workflow; production
code does not use reflection to bypass these boundaries.
