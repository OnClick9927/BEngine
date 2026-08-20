---
name: bengine-animation
description: "Develop and use the optional BEngine Animation package, including fixed-point curves, clips, events, Animator controllers, transitions, parameters, YAML animation assets, and editor registration. Use when implementing animation playback or state machines, authoring .anim.yaml or .controller.yaml assets, or changing code under src/Animation."
---

# BEngine Animation

## Respect the package contract

- Use package ID `com.bengine.animation`; it is disabled by default and has no runtime package dependency beyond Core.
- Put runtime code in `BEngine.Animation` and editor-only registration or GUI in `BEngine.Animation.Editor`.
- Let the editor assembly reference runtime and `BEngine.Editor`; never reference editor APIs from runtime.
- Store runtime assets in `Resources` and editor icons or tools in `EditorResources`.

## Work with the runtime API

- Use `AnimationCurve`, `Keyframe`, `AnimationClip`, `AnimationBinding`, and `AnimationEvent` for clip data and sampling.
- Use `Animator`, `AnimatorController`, states, transitions, conditions, and typed parameters for controller-driven playback.
- Keep animation time, values, tangents, speeds, thresholds, and blend decisions deterministic with `Fix64`.
- Preserve `Animation` and `Animator` as scene behaviours and clips/controllers as `BObject` assets.

## Preserve YAML and compatibility

- Convert `.anim.yaml` and `.controller.yaml` through the package document converters; validate documents through `DocumentValidationRegistry`.
- Register new animation document types through `DocumentConversionRegistry`, not ad hoc YAML parsing.
- Maintain legacy component migrations in `AnimationPackageRegistration` when moving runtime type names.
- Register asset extensions and icons from the editor assembly with `AssetTypeRegistry` and `EditorIconRegistry`.

## Change safely

- Keep sampling and state transition order stable across runs; do not introduce wall-clock or floating-point state into runtime evaluation.
- Keep controller authoring UI, previews, and inspectors out of the runtime assembly.
- Update `package.yaml` when dependencies or assembly boundaries change, and add focused package tests under `Example/Tests`.
