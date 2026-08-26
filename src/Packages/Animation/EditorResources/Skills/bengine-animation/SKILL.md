---
name: bengine-animation
description: "Develop and use the optional BEngine Animation package, including fixed-point curves, clips, events, Animator controllers, transitions, parameters, YAML animation assets, and editor registration. Use when implementing animation playback or state machines, authoring .anim.yaml or .controller.yaml assets, or changing code under src/Packages/Animation."
---

# BEngine Animation

## Enable the package and import examples

- Open `Window/Package Manager`, select `Animation`, and press `Import`. The package ID is `com.bengine.animation`, it is disabled by default, and it has no optional runtime dependency.
- In the package's `Examples` tab, import each example separately. `Animation Getting Started` installs to `Assets/Examples/AnimationGettingStarted`; open `res/Animation.scene.yaml`, enter Play, press Space to pause/resume, and use Up/Down to change speed while watching the sprite and Console events.
- `Animator State Machine` installs to `Assets/Examples/StateMachine`; open `res/StateMachine.scene.yaml`, enter Play, press Space to change the bool-driven Idle/Spin state and Enter to set the one-shot Pulse trigger.
- The button changes to `Reimport` after installation. Reimport preserves locally modified files, repairs missing files, updates unchanged files, and reports `Partial`, `Modified`, `Update available`, or `Imported`. Example import is disabled while the package is disabled or the editor is in a Play-mode transition/session.

## Create and inspect animation assets

- Create clips with `Assets/Create/Animation/Animation Clip`; the output suffix is `.anim.yaml`. Configure `frameRate`, `wrapMode`, `legacy`, `bindings`, and `events` in Inspector, then Apply or Revert the `BAsset` changes.
- A binding contains `relativePath`, the full `componentType` name, `propertyName`, and an `AnimationCurve`. Keyframes use fixed-point time/value/tangents. An event contains fixed-point `time`, `functionName`, and string/int/fixed-point parameters.
- Create state machines with `Assets/Create/Animation/Animator Controller`; the output suffix is `.controller.yaml`. Configure `defaultState`, typed parameters, and states. Each state has a unique name, clip path, speed, loop flag, and transitions; each transition has destination, optional exit time, duration metadata, and conditions.
- Project selection resolves both types through `AssetTypeRegistry`, uses the Animation asset icon, and displays the generic BAsset Inspector/preview. There is currently no Animation timeline, curve editor, controller graph, custom Animation Inspector, or Animation-specific `EditorWindow`; do not instruct users to open one.
- The package registers no Animation-specific `Tools/...`, `GameObject/...`, or `Window/...` command; use the asset and Component paths above rather than inventing shortcuts.
- Current `Animation.clipPath`, `Animator.controllerPath`, and state `clipPath` are strings resolved relative to `Application.dataPath` unless absolute. Use a path relative to the project's `Assets` directory with these loaders; do not prepend another `Assets/` segment and do not describe them as ObjectField references until the package migrates them.

## Add and configure scene components

- Select a GameObject and use Inspector `Add Component` or the top `Component` AdvancedDropdown. Add `Animation/Animation` for one clip or `Animation/Animator` for a controller. Both disallow duplicate instances.
- On `Animation`, set `clipPath` and `playAutomatically`. During Play, `isPlaying` shows runtime state; script control is `Play()` and `Stop()`.
- On `Animator`, set `controllerPath`, `speed`, and `playOnAwake`. `runtimeAnimatorController` is hidden and is for a runtime object assigned from code. Read-only state includes `currentStateName` and `normalizedTime`.
- Drive controllers from scripts with `Play`, `HasState`, float/int/bool setters and getters, `SetTrigger`, and `ResetTrigger`. Conditions support `If`, `IfNot`, `Greater`, `Less`, `Equals`, and `NotEqual`.
- Component context menus still provide Core `Reset`, copy/paste, Edit Script, and removal. Animation adds no custom context command and no Scene Gizmo.

## Debug playback without changing assets

- Open the imported scene from Project, select its animated GameObject in Hierarchy, and keep `Window/General/Scene`, `Game`, `Inspector`, and `Console` open. Enter Play and inspect sampled Transform/component values, `currentStateName`, `normalizedTime`, and event output. Use Pause/Step to inspect deterministic fixed-point advancement.
- Animation events search enabled `MonoBehaviour` components on the animated GameObject and invoke a matching method with `AnimationEvent` when accepted, otherwise a parameterless matching method. Use Console for missing or throwing callbacks.
- Stopping Play discards the runtime scene clone, including component values, controller parameters, triggers, runtime-created clips/controllers, and sampled transforms. Play-mode changes cannot be applied to `.anim.yaml`, `.controller.yaml`, scene, prefab, or component assets.
- Be explicit about current limits: `CrossFade` immediately calls `Play` and does not blend; `IsInTransition` always returns false; transition `duration` is not evaluated; `applyRootMotion` is exposed but not applied; `layer` arguments are accepted but there is no layered controller implementation. Do not document these as working features.

## Extend the package

- Keep runtime code in `BEngine.Animation` and editor code in `BEngine.Animation.Editor`; the editor assembly may reference runtime and Core Editor, but runtime must not reference editor APIs.
- Add scene behaviours with `[AddComponentMenu("Animation/...")]`. Add authorable animation assets with `[CreateAssetMenu]`, a stable suffix via `AssetTypeRegistry.Register<TAsset>`, and an EditorResources icon via `[EditorIcon]` or `EditorIconRegistry.Register`.
- Persist new YAML models through `DocumentConversionRegistry` and validate them through `DocumentValidationRegistry`. Update component legacy names in `AnimationPackageRegistration` when types move.
- A custom authoring window must derive `EditorWindow`, register a real `Window/...` `[MenuItem]`, use `SerializedObject`/Undo/dirty state, and call the existing asset `Save` path only when `EditorAssetWritePolicy.CanWrite`.
- A custom clip/controller Inspector uses `[CustomEditor]`; a curve/member drawer uses `[CustomPropertyDrawer]`. Put all GUI and icons below the editor assembly/`EditorResources`.
- Preserve deterministic state order, fixed-point curves, stable event ordering, and cached reflection. Do not introduce wall-clock or floating-point simulation state.

## Validate and troubleshoot

- If menus or components are absent, confirm the package is imported, its runtime/editor assemblies compiled, and Console has no errors. If an asset is shown as a generic file, confirm `.anim.yaml` or `.controller.yaml` and the editor registration loaded.
- If playback does nothing, verify the resolved file exists, controller `defaultState` matches a state, the state clip path exists, bindings use an actual component type/property, and `playAutomatically` or `playOnAwake` is enabled.
- Cover clip load/save, sampling, events, typed parameters, transition conditions, deterministic repeatability, package disable/unload, and Play-mode asset-write isolation under `Example/Tests/Animation` or the focused package test boundary.
