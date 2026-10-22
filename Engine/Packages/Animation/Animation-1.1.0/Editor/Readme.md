# Animation package

The package owns Animation and Animator runtime assets and their
authoring workflow. UIElements supplies the reusable `NodeCanvas` and
`TrackEditorCanvas` templates so other packages can build compatible editors.

When a selected GameObject has an Animator/Animation that matches the
asset open in a window, the editor displays the live state or playhead while
Play Mode is running.

Runtime implementation lives in `BEngine.Animation`; authoring windows,
inspectors, preview/recording and asset registration live in `BEngine.Animation.Editor`.
The public namespaces remain `BEngine` and `BEngine.Editor`.
Timeline is distributed separately as `com.bengine.timeline` and depends on this package.

Animation tracks support Fix64/integer values, Boolean steps, individual Color/Vector2
channels, and discrete Sprite/asset references. Use Add Property or Record in the Animation
window. Object keys use an asset picker; copying keys between clips preserves their references.
Preview/recording restores authored values on Stop and supports Undo/Redo.

Numeric keys offer Free, Linear, Constant, Auto and ClampedAuto tangents, with separate
incoming/outgoing weights. Select a key to edit modes and weights below the timeline.
Dragging a tangent switches that side to Free. Sampling and weighted Bezier time inversion
use Fix64. Duplicate times replace the existing key.

Clip documents now write version 2, using the engine object graph for asset references and
tangent metadata; version 1 clips still load. Runtime-created references need a persistent
asset identity before saving. Animator and Timeline interpolate numeric channels and choose
the dominant contribution for Boolean/object channels (the later contribution wins a tie).

Validation: `Temp/Validation/AnimationEnhancements` covers playback, serialized Sprite references,
bundle dependency collection, old clips, recording, key editing, Undo/Redo and Play write guards.
`Temp/Validation/AnimationPackage.cs` compiles the exported source package and verifies reload.

Animator also supports Cartesian 2D gradient-band blending and nested trees (16 levels,
512 trees, 128 motions per tree). X/Y parameters drive sample positions; coincident samples
share their location's contribution. Trees reject cycles. Parameter edits preserve normalized
playback progress when the effective motion duration changes. State clipboard and parameter
rename/removal traverse nested trees. Controller documents write version 2 and still read v1.

Additional layers can use Override or Additive blending and transform-path masks. An empty
mask includes all paths; a nonempty mask includes active paths and optional descendants,
with inactive entries taking precedence. Additive numeric deltas are relative to the original
authored pose or an assigned reference clip sampled at time zero. Unanimated discrete
properties retain their previous layer's value.

A synchronized layer follows a preceding layer's state, phase and crossfade. States with
matching full state paths supply alternate motions; absent overrides use the source motion. The source
state machine owns transitions and events. Only the base layer supplies root motion.
Layer reordering preserves sync references and prevents moving a follower before its source.
Sub-state machines use qualified paths such as `Movement/Ground/Run`. Create one in the
Animator graph context menu, double-click to enter it, and use the breadcrumb toolbar to
return to a parent. Entry, Any State, default targets, outgoing machine transitions and graph
positions are stored per machine. Names must be unique within each graph; duplicate leaf
names in separate machines are allowed.

Transition destinations may be local, `../` relative, or `/` absolute. Targeting a machine
selects its first valid conditional entry, falling back to its default recursively. Root Any
State transitions take priority over descendant Any State transitions, followed by the leaf
state's transitions. On Exit, the machine's outgoing transitions are evaluated in its parent
graph; if none applies, Exit propagates upward. Root Exit stops the layer until Play restarts it.
Entry and outgoing machine routes are immediate (their conditions/offsets apply); the initiating
leaf/Any State transition controls fade duration and exit time. Invalid/cyclic routes do not
change playback or consume triggers. Hierarchies reject shared children/cycles and exceedance
of 16 levels or 4096 machines per layer.

Rename and deletion repair incoming/default references using resolved paths. Undo restores
the hierarchy, references and graph positions. The state clipboard preserves external paths
when moving states between graphs. Version 2 controllers include the hierarchy; v1 flat
controllers remain readable.


The Windows packaged Player acceptance runs the real Vulkan game loop and checks Sprite
pixels, typed tracks, weighted tangents, nested controllers, Timeline seek/restore, nested
2D blends, additive reference poses, synchronized layers and masks. It also checks a content
update from v1 to v2. Bootstrap-only `--validate` does not execute the probe's Start callback
and is not sufficient evidence of animation behavior. See `Temp/Validation/Reports/EngineCompletionChecklist.md`
for the retained logs and `Editor/Doc/Animation/PlayerValidation.md` for reproduction.
