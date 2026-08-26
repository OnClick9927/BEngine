---
name: bengine-core
description: "Develop and use BEngine core runtime, editor host, launcher, player, YAML document, lifecycle, and rendering APIs. Use when changing code under src/Core, adding a core BEngine or BEngine.Editor API, extending GPU IMGUI, serialization, assets, scenes, or the graphics abstraction, or deciding whether functionality belongs in core rather than an optional package."
---

# BEngine Core

## Open the editor and locate its tools

- Start the editor with `Output/BEngine.bat` and select a project whose authored content is below `Assets`.
- Open the primary panels from `Window/General/Console`, `Window/General/Game`, `Window/General/Hierarchy`, `Window/General/Project`, `Window/General/Inspector`, and `Window/General/Scene`. Open `Window/Package Manager` for packages and examples.
- Use `Window/Editor Status` for editor, graphics, package, service, and log diagnostics; `Window/Codex` opens the built-in Codex surface.
- Use `Window/Rendering/Sorting Layers` or `Edit/Sorting Layers...` for render layer names. Use `Edit/Project Settings...` for `Asset Bundles`, `Graphics`, `Player`, `Scripting`, and the separate `Tags` and `Layers` tabs under `Tags and Layers`.
- Use `Edit/Preferences...` for language, scale, font, theme, asset refresh, meta-file visibility, and the external script editor.
- File operations are `File/New Scene`, `Open Scene...`, `Open Scene Additive...`, `Save Scene`, `Save All Scenes`, `Show Project in Explorer`, and `Exit`. Save commands are unavailable when the Play-mode write policy is active.
- Selection/edit operations are `Edit/Undo`, `Redo`, `Copy`, `Paste`, `Duplicate`, `Rename`, `Delete`, `Select All`, `Deselect All`, and `Frame Selected`; their validators follow the focused Project/Hierarchy/Scene context.
- Manage the focused dock with `Window/Panels/Close Focused Tab`, `Lock Focused Window`, `Maximize Focused Tab`, `Next Window`, and `Previous Window`. Use `Help/Documentation`, `View Editor Log`, `Reveal Logs Folder`, `Copy System Info`, and `About BEngine` for help/diagnostics.
- Import the required Core example from `Window/Package Manager > BEngine Core > Examples > Core Getting Started > Import`. It is installed below `Assets/Examples/CoreGettingStarted`; open `res/Core.scene.yaml`, enter Play mode, use the configured Horizontal/Vertical axes and Space, and inspect lifecycle/coroutine output in Console. The imported editor assembly also registers `Tools/Examples/BEngine Editor Window`.

## Work with Project assets

- The Project context menu and top `Assets` menu use the same `MenuItem` registry, including entries contributed by project scripts and packages. `/` creates an AdvancedDropdown subtree.
- Create built-in content with `Assets/Create/Folder`, `C# Script`, `Scripting/ScriptableObject Script`, `Assembly Definition`, `Scene`, `Prefab`, `Shader`, `Text/Text File`, `Text/Markdown File`, `Data/JSON File`, and `Data/YAML File`.
- Create authored render assets with `Assets/Create/Rendering/Material`, `Assets/Create/2D/Sprite`, and `Assets/Create/2D/Texture Atlas`. Material and Atlas creation are functional generic `CreateAssetMenu` flows. A Sprite must already contain a valid texture reference when saved, so do not claim the current empty `Assets/Create/2D/Sprite` command is a complete PNG-to-Sprite authoring workflow.
- Use `Assets/Open`, `Show in Explorer`, `Copy Path`, `Copy Full Path`, `Rename`, `Duplicate`, `Delete`, `Reimport`, `Refresh`, and `Import New Asset...` on the current Project selection. Scenes also expose `Assets/Open Scene/Additive` and `Additive Without Loading`.
- Use `Assets/Import Package...` and `Export Package...` for `.bpackage` archives. Use `Assets/Build Asset Bundles...` after configuring `Project Settings/Asset Bundles`.
- Select a texture, font, shader, script, scene, prefab, material, sprite, atlas, or other `BAsset` to inspect its type-specific preview. Drag the Preview splitter to resize it and use the foldout to collapse it.
- Texture import settings are `compressionFormat`, `filterMode`, `wrapMode`, `generateMipMaps`, `maxTextureSize`, and `pixelsPerUnit`; font settings are `defaultSize`, `includeKerning`, and `characterSet`; shader settings are `strictCompilation` and `optimizationLevel`. Apply or Revert in the FileAsset Inspector explicitly.

## Author scenes and components

- Create hierarchy objects with `GameObject/Create Empty`, `Create Empty Child`, `Create Empty Parent`, `2D Object/Sprite`, `Effects/Particle System 2D`, or `Camera 2D`. The Sprite command adds `SpriteRenderer`; the particle and camera commands add their matching components.
- Use `GameObject/Set Active` or `Set Inactive`; `GameObject/Transform/Reset`, `Reset Position`, `Reset Rotation`, or `Reset Scale`; and `GameObject/Hierarchy/Select Parent`, `Select Children`, `Move To Root`, `Move Up`, `Move Down`, `Set As First Sibling`, `Set As Last Sibling`, or `Center On Children`. `GameObject/Frame Selected`, `Copy Hierarchy Path`, `Rename`, `Duplicate`, and `Delete` act on the current selection.
- Add components from the Inspector's `Add Component` AdvancedDropdown or the top `Component` menu. Core paths are `Rendering/Camera 2D`, `Rendering/Sprite Renderer`, and `Effects/Particle System 2D`; external `[AddComponentMenu]` components appear automatically.
- Configure `Camera2D` with orthographic `size`, `backgroundColor`, `isMain`, render `priority`, `clearMode`, power-of-two `cullingMask`, and normalized `viewportRect`. Camera rendering is priority ordered; component context commands include `Align With View` and `Move To View`.
- Configure `SpriteRenderer` with a `Sprite` reference, size, pivot or Sprite pivot, color, flip X/Y, Material, sorting layer, order, and opacity. The renderer discovers Atlas membership from the Sprite asset; it has no Atlas field.
- Configure `ParticleSystem2D` duration/loop/play-on-awake, emission, lifetime, speed/direction, size/color/rotation, angular velocity, maximum count, legacy Atlas/region fields, Material, sorting layer, order, and opacity. Its sorting layer may be one of the five UI layers, allowing particles among UI submissions.
- Component header context actions are `Reset`, `Copy Component`, `Paste Component Values`, optional `Edit Script`, inherited custom `[ContextMenu]` entries, and `Remove Component`. Reset invokes `Component.OnReset`; `MonoBehaviour.OnReset` delegates to virtual `Reset()`.
- Use `Tools/Remove Missing Components` for missing scripts. Use GameObject Prefab commands `Save As Prefab Asset`, `Open Prefab`, `Apply All`, `Revert All`, and `Unpack`.
- Use the Hierarchy's leftmost eye and picking controls to hide an object from Scene view or disable Scene picking, including descendants. These are editor-only visibility states.

## Use Scene and Game views

- In Scene view, Q/W/E/R/T select View, Move, Rotate, Scale, and Rect tools. Handles remain at the selected GameObject's aggregate visual center and use distinct move arrows, rotation ring, scale squares, and rect outline.
- Scroll zooms; middle-button drag pans; right-button drag orbits the editor camera. Click a rendered object to select it in both Scene and Hierarchy; repeated clicks at the same point cycle overlapping visible, pickable objects.
- Toggle all Gizmos from the Scene toolbar. Its dropdown can enable `All`, `None`, or individual component types under `Components/...`, including types from loaded external assemblies.
- In Game view, choose Free Aspect, a built-in resolution group, or a custom resolution. `Add Custom Resolution...` and the custom delete submenu manage project choices. Expand `Status` for FPS/frame time, camera, visible submission, batch, draw, triangle, vertex, screen, and graphics completeness data.
- Press Play from the toolbar or `Edit/Play` (`Ctrl+P`); an open Game view receives focus. `Edit/Pause` and `Edit/Step` control debugging. Use Console filtering/collapse/error-pause and Game Status while running.
- Play mode runs a cloned scene session. Inspector, Hierarchy, Scene, and Game show runtime objects while playing; stopping discards the clone and immediately restores authored scene/object/component state. Runtime mutations must never be saved back to scene, prefab, component, or other asset files.
- `EditorAssetWritePolicy` blocks asset creation, import/reimport, example import, atlas build/save, and other authoring writes while entering, running, or exiting Play mode. Do not bypass it from editor-facing write APIs.

## Build sprites and atlases

- Store Sprites as `*.sprite.yaml` files containing a stable PNG texture reference and normalized pivot. Store atlases as `*.atlas.yaml` plus the generated PNG.
- Open `Window/2D/Texture Atlas`. New/Save/Reload control the manifest; set Max Size, Padding, and Extrude; add `*.sprite.yaml` paths manually or select Sprite assets in Project and choose `Add Selected`; then Build.
- The builder requires at least one valid Sprite (or a legacy source), PNG inputs, unique Sprite references/names, deterministic power-of-two packing, and `Extrude <= Padding`. The window previews the generated Atlas.
- Assign only a `Sprite` to `SpriteRenderer.sprite`. `TextureAtlasResolver` finds any Atlas that references it and supplies UV, pivot, and batch identity; an unpacked Sprite uses its own texture. Batching requires adjacent sorted submissions with the same Material, Shader, and Atlas/texture identity.

## Preserve runtime architecture

- Put portable runtime APIs in `src/Core/BEngine`; keep them independent of `BEngine.Editor` and optional packages. Put editor-only APIs and GPU IMGUI in `src/Core/BEngine.Editor`.
- Keep project selection/history in `BEngine.Launcher`, and exported-app startup/package loading in `BEngine.Player`. Load runtime files from `Resources` and editor files from `EditorResources`; Core intentionally has no `package.yaml`.
- Derive engine objects from `BObject`; use `GameObject`, `Component`, `Behaviour`, `MonoBehaviour`, and `ScriptableObject` according to lifecycle needs. Use `Fix64`, `Vector2`, and scalar angles for runtime simulation.
- Serialize through `YamlUtility`, `Document`, and registered converters/validators. Persist asset references as stable `Assets/...` or `Packages/...` paths and preserve concrete `$type` sidecars for base-typed asset fields.
- Keep layer values as powers of two from `2^1` through `2^63`; `2^1..2^58` are World and the highest five are UI. Rendering sorts by layer, order, hierarchy, transparency, then stable submission order.

## Extend the editor accurately

- Add a static method with `[MenuItem("Root/Submenu/Command", false, priority)]` and an optional same-path validator `[MenuItem(..., true)]`. `Assets`, `GameObject`, and `Component` contributions are shared by top menus and relevant context/AdvancedDropdown surfaces.
- Add `[AddComponentMenu("Group/Name", order)]` to a concrete `Component`; types without it appear as `Scripts/TypeName` for `MonoBehaviour` or as their type name. `[DisallowMultipleComponent]` disables duplicate addition.
- Add `[CreateAssetMenu(fileName = ..., menuName = ..., order = ...)]` to a concrete `BObject`. Register a stable suffix and loader with `AssetTypeRegistry.Register<TAsset>` when it is not the generic `.asset.yaml` format.
- Create an `EditorWindow`, expose it through a `Window/...` `[MenuItem]`, and use `GetWindow<T>`, `Show`, `ShowAuxWindow`, `ShowPopup`, or `ShowAsDropDown`. Implement `OnGUI` and only the lifecycle callbacks actually needed.
- Use `[CustomEditor(typeof(T), editorForChildClasses)]`, `[CustomPropertyDrawer(typeof(T), useForChildren)]`, and preview overrides `HasPreviewGUI`, `OnPreviewGUI`, and `GetInfoString`. Use `SerializedObject`, `SerializedProperty`, Undo, and `EditorUtility.SetDirty` for authored changes.
- Use `EditorGUI.ObjectField` or `EditorGUILayout.ObjectField` for `BObject` references. Both expose typed/generic and `SerializedProperty` overloads; pass `allowSceneObjects: true` only when scene-object references are valid for the field.
- Use `[SettingsProvider]` to return a `SettingsProvider` under `Preferences/...` with `SettingsScope.User` or `Project/...` with `SettingsScope.Project`.
- Add inherited component commands with `[ContextMenu("Group/Command")]` on an instance `void` method with no parameters. Add Scene drawings with `[DrawGizmo]`; package-rendered pickable geometry also registers a `ScenePickingProviderRegistry` provider.
- Register asset/editor icons from a package's `EditorResources` through inherited `[EditorIcon("relative/path.png")]`, `[EditorWindowIcon]`, or `EditorIconRegistry.Register`. Use `[OnOpenAsset]` for custom asset opening and an `AssetImporter` subclass for source settings.
- Extend runtime rendering with `SceneRenderContributor2DRegistry`, scene work with an `ISceneRuntimeSystem` registered through `RuntimeSystemRegistry` or an `IEngineServiceModule`/DI service, and YAML with `DocumentConversionRegistry` plus `DocumentValidationRegistry`. Registrations must be owned by the loaded assembly so package unload can remove them.
- Keep GPU IMGUI allocation- and reflection-light in repaint/update loops; route reflective member access through cached registries such as `TypeCache` and `EditorReflectionCache`.

## Validate and troubleshoot

- Use `Edit/Recompile Scripts`, Console, `Window/Editor Status`, `Help/View Editor Log`, and `Help/Reveal Logs Folder` to diagnose compile, package, asset, rendering, or editor failures.
- When an asset does not appear, verify its suffix registration, `.meta`, stable project path, package enabled state, and then use `Assets/Reimport` or `Refresh`. When a component/menu/gizmo is absent, verify its runtime/editor assembly boundary and discovery attribute signature.
- Add focused tests under `Example/Tests/<feature>` and validate the narrowest affected runtime, editor, launcher, or player assembly. Include a Play-mode write-isolation regression for any new authoring API.
