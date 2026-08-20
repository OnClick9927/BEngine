---
name: bengine-core
description: "Develop and use BEngine core runtime, editor host, launcher, player, YAML document, lifecycle, and rendering APIs. Use when changing code under src/Core, adding a core BEngine or BEngine.Editor API, extending GPU IMGUI, serialization, assets, scenes, or the graphics abstraction, or deciding whether functionality belongs in core rather than an optional package."
---

# BEngine Core

## Locate the change

- Put portable runtime APIs in `src/Core/BEngine`; keep them independent of `BEngine.Editor` and every optional package.
- Put editor-only APIs and GPU IMGUI in `src/Core/BEngine.Editor`; it may reference `BEngine` but no package assembly.
- Keep project selection and history in `BEngine.Launcher`, and exported-app startup/package loading in `BEngine.Player`.
- Load runtime files from `Resources` and editor files from `EditorResources`; do not embed non-code files in assemblies.

## Preserve core boundaries

- Keep `BEngine` unaware of Package Manager and of concrete package IDs, types, or assemblies.
- Let `BEngine.Editor` discover packages from project/package directories. Do not hard-code the available package catalog into runtime.
- Keep optional systems such as animation, Navigation2D, Physics2D, property attributes, and UIElements in their packages.
- Treat Core as the engine distribution root; it intentionally has no `package.yaml`.

## Follow engine conventions

- Derive engine objects from `BObject`; use `GameObject`, `Component`, `Behaviour`, `MonoBehaviour`, and `ScriptableObject` according to lifecycle needs.
- Use `Fix64` and BEngine math structs for deterministic simulation state. Convert to floating-point only at rendering or platform boundaries.
- Serialize through `YamlUtility`, `Document`, and registered document converters. Keep generic YAML mechanics separate from concrete document models.
- Register runtime systems and initialization callbacks once during startup; use cached type metadata instead of reflection in update or draw loops.
- Extend rendering through RHI interfaces, backend providers, or `SceneRenderContributor2DRegistry`. Submit Sprite, Particle, and UI work to the shared ordering queue; batch only adjacent items with matching Material, Shader, and Atlas.
- Store authored sprite atlases as `*.atlas.yaml` plus their generated PNG. Keep source names and paths unique, pack deterministically into power-of-two dimensions, and preserve padding/extrusion so filtered sampling cannot bleed across regions.
- Set `SpriteRenderer.atlas` or `ParticleSystem2D.atlas` to the atlas asset and `sprite` to a region name. A direct PNG path in `sprite` remains valid when no atlas is assigned.
- Keep the runtime 2D-only: positions/scales are `Vector2`, rotations are scalar angles, and render layers are power-of-two `ulong` values. The highest five layers are reserved for UI.

## Extend the editor

- Use `EditorWindow`, `MenuItem`, `CustomEditor`, `PropertyDrawer`, `SettingsProvider`, asset processors, and editor lifecycle attributes.
- Keep IMGUI in `BEngine.Editor`; batch GPU canvas commands and avoid per-frame allocation or reflection.
- Keep editor state scoped correctly: preferences in editor data, project settings in the project, and layouts through the layout store.
- Add focused coverage under `Example/Tests/<feature>` for changed contracts and validate the narrowest affected assembly boundary.
