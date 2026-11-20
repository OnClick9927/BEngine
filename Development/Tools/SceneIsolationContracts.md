# Scene isolation and clock ownership

Core Editor provides `EditorSceneIsolation.CloneScenes` for caller-owned scene mirrors.
The caller explicitly supplies scenes and additional asset roots, then owns every
value in the returned source-to-mirror map. Dispose scene values and call
`ReleaseAssets` for non-scene values when retiring the graph. A failed construction
cleans up its partial graph. References to hierarchy objects outside the supplied
scenes fail rather than retaining live editable objects. Additional selected scene
assets do not implicitly join the mirrored world.

Construction defers serialization callbacks. Call `InvokeAfterDeserialize` only
after the consumer has installed its runtime context. It deduplicates objects and
uses the owning scene, with a supplied fallback for standalone assets. It does not
perform editor validation or choose a Play Mode policy.

Core Editor no longer knows Material, Shader or Sprite. The existing
`RuntimeObjectCodec` contract now has `CreateCloneInstance` for empty instances.
`BObject` provides protected `CaptureClonePayload` / `RestoreClonePayload`, matching
the pre-existing BAsset contract. BAsset keeps its declared overrides and internal
compatibility overload. Payloads use CLR/Core types and map every object reference
through the supplied mapper, allowing current assembly instances to restore data
from a retiring collectible domain. Graphics owns Shader payload/construction;
Rendering2D owns Sprite payloads. Ordinary Instantiate semantics are unchanged.

The workbench still chooses open scenes, selections, Inspector locks, Undo history,
dirty state, Prefab Stage and reload commit/rollback policy. Core owns only the
generic graph mechanism; no workspace UI is moved into Core.

`Time.CaptureState` / `RestoreState` use an opaque value snapshot between frames,
including the pending fixed-step remainder and timing settings. They reject active
frames and uninitialized snapshots. Realtime and the monotonic frame sequence keep
advancing. SceneRuntime deduplicates dispatch by that sequence, so rewinding the
public simulation frame count cannot skip the next frame. The workbench uses this
mechanism instead of writing seven internal Time properties.

Validation: `Test-SceneIsolationContracts.ps1` covers independent headless consumers,
clock remainder and frame dispatch, shared/cyclic graphs, excluded scene references,
Prefab identity/order, provider failures, graphics payloads, collectible assembly
rebinding, and workbench reload commit/rollback. Validation output belongs under
the workspace Temp directory. Zero warmed clock allocation is a local measurement,
not a claim about whole-editor startup, Player size, rendering or memory gains.
