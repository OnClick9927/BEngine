---
name: bengine-ui-elements
description: "Develop and use the optional BEngine UIElements retained-mode UI package, including VisualElement trees, controls, layout, styling, UXML, USS, UIDocument, GPU rendering, UI Builder, and HTML conversion. Use when changing code under src/Packages/UIElements or building runtime and editor retained-mode interfaces instead of core GPU IMGUI."
---

# BEngine UIElements

## Enable the package and import examples

- Open `Window/Package Manager`, select `UIElements`, and press `Import`. Package ID `com.bengine.ui-elements` is disabled by default and depends only on Core at runtime; AngleSharp packages remain editor-only.
- Import `Controls Gallery`. It installs to `Assets/Examples/ControlsGallery`; open `res/ControlsGallery.scene.yaml`, enter Play, edit the fields, and use Apply Preset/Reset to inspect UXML, USS, `Q<T>`, value changes, and common controls.
- Import `Runtime HUD` separately. It installs to `Assets/Examples/RuntimeHud`; open `res/UIElements.scene.yaml`, enter Play, and click Advance Progress to update the progress bar and programmatically built TreeView data.
- Reimport preserves modified example files and repairs/updates the rest. Import/Reimport is blocked until the package is enabled and while Play mode disallows project writes.

## Create UI source files

- Use `Assets/Create/UI Toolkit/UI Document` in the top Assets menu or Project context menu to create a minimal `.uxml` text file. Use `Assets/Create/UI Toolkit/Style Sheet` to create `.uss`.
- These commands create source files, not registered `BAsset` subtypes: there is no UIElements `AssetTypeRegistry` registration, custom UXML/USS Inspector, or `[OnOpenAsset]` callback. Project double-click does not currently open a UXML document in UI Builder.
- Open `Window/UI Builder` to create a new document. Its toolbar supports New, Save, HTML Converter, Add Container, Add Label, Add Button, and Add Text Field. The hierarchy selects elements; Inspector edits name, text/value, flex direction, width, height, flex grow, font size, and Delete.
- New UI Builder documents save by default to a generated `Assets/UI/New UI.uxml` path. The present window has no visible Open/Load control even though an internal `Open(path)` helper exists; do not instruct users to load an existing Project UXML through UI Builder.
- The Builder's `GPU Preview` area is a simplified recursive IMGUI box representation, not the actual runtime GPU renderer or a complete style/layout preview. It does not expose every runtime control or USS property.

## Convert HTML

- Open `Tools/UIElements/HTML Converter`, or select an `.html`/`.htm` Project file and use `Assets/Convert HTML to UIElements`.
- Choose source HTML and destination UXML, then select whether to copy local resources and generate a C# binding. Convert writes `<name>.uxml`, `<name>.uss`, optional `<name>.generated.cs`, and `<name>.conversion.yaml` beside the destination.
- Inspect the conversion summary and every diagnostic. Unsupported/lossy HTML, CSS, scripts, remote resources, and unsupported tags must remain reported; the converter validates that the produced UXML loads and emits render commands but does not claim browser equivalence.
- Converter preferences remember only source/destination paths in EditorPrefs. `Edit/Preferences... > Packages/UIElements` currently shows package information and an Open UI Builder button; `Edit/Project Settings... > Packages/UIElements` is informational and exposes no editable runtime setting.

## Add and configure runtime UI

- UIElements has no GameObject creation command. Create/select a GameObject, then add `UI Toolkit/UI Document` through Inspector `Add Component` or the top Component AdvancedDropdown. It disallows duplicates.
- UIElements registers no `[DrawGizmo]` visualization or Scene handle. Select and configure `UIDocument` through Hierarchy/Inspector and validate its rendered result in Game view.
- Set `sourceAsset` to an absolute path or a path relative to the project's Assets directory, `sortingOrder`, one of the five UI `sortingLayer` values, Material, Atlas path, interactable state, scale mode, and reference width/height.
- `UIDocument` reloads on enable or when `sourceAsset` changes. An empty/missing source falls back to an empty Root and logs IO/data/access failures to Console.
- Build code trees from `VisualElement` and controls including `Label`, `Button`, `TextField`, `SearchField`, `FloatField`, `IntegerField`, `ColorField`, `Toggle`, `Slider`, `DropdownField`, `ProgressBar`, `Foldout`, `Image`, `Box`, `ScrollView`, `ListView`, `TreeView`, and Toolbar controls. Use `VisualTreeAsset.Load(...).Instantiate()`, `Q<T>`, class lists, and `StyleSheet` parsing/application.
- UXML supports the package's hard-coded control names and serialized properties. Structure belongs in UXML, style in USS, and behavior/event registration in C#.
- Each VisualElement may override UI sorting layer, order, Material, and Atlas; otherwise it inherits the UIDocument values. Runtime rendering adds document sorting order to element order and batches adjacent commands only when Material/Shader/Atlas identity matches.

## Run and debug UI

- Open Scene, Game, Inspector, and Console, then enter Play. Use Game resolution choices to test `ConstantPixelSize` and `ScaleWithScreenSize`; inspect Game Status for draw/batch counts and Console for UXML/resource failures.
- `UIDocument` currently dispatches a mouse-down only to `Button` ancestors, and only the highest `sortingOrder` enabled/interactable document receives it. TreeView/ListView mouse selection, text entry, sliders, and general pointer/focus routing are not connected by the runtime document dispatcher; examples use buttons and programmatic APIs accordingly.
- `PanelSettings` exists as a ScriptableObject API with scale mode/reference resolution, but UIDocument duplicates these fields and has no PanelSettings reference or asset creation command. Do not instruct users to create/assign a PanelSettings asset.
- Stop Play to discard runtime-created trees, values, selection, events, and UIDocument/component changes. Runtime UI cannot save UXML, USS, generated bindings, scene, prefab, Material, or Atlas assets.

## Extend the package

- Keep retained-mode runtime types in `BEngine.UIElements` and UI Builder/HTML conversion in `BEngine.UIElements.Editor`. Core editor windows remain GPU IMGUI and Core must not reference this optional package.
- Register the runtime UI renderer through the actual `SceneRenderContributor2DRegistry` API, as `UIElementsPackageRegistration` does. There is no `SceneOverlayRendererRegistry`; do not use or document that nonexistent name.
- A new code-only control derives `VisualElement` or an existing field/control. To load a new control name from UXML, extend the serializer's explicit element factory, save logic, and package-internal DTO mapping; there is currently no public UXML factory registry for external types.
- Extend UI Builder only in the editor assembly, use real `Window/...` or `Tools/UIElements/...` `[MenuItem]` paths, Undo/dirty/write policy for project files, and accurate runtime preview claims.
- Extend HTML conversion through `HtmlConversionOptions`, diagnostics/report models, DOM/CSS mappings, and round-trip/runtime-load validation. Keep AngleSharp out of runtime.
- Keep UI YAML payloads as package-internal DTOs and persist them through `YamlUtility`; `VisualTreeAsset` is the public `BAsset` boundary and playback assets are read-only. Store shaders below `Resources/Shaders/UIElements` for the supported backends and render only through RHI/resource resolver abstractions.
- Keep TreeView selection, expansion, rename, drag/drop and drop-marker code deterministic. Those APIs are usable programmatically even where runtime pointer dispatch is not yet wired.

## Validate and troubleshoot

- If menus/component are absent, verify UIElements is imported and runtime/editor assemblies compiled. If UI is blank, verify `sourceAsset` resolution, file existence, UXML element names, USS source paths, Camera UI layer mask, enabled/active document, color/size, Material shader, and Console errors.
- If input does nothing, first test a Button in the highest-order interactable document; do not expect other control pointer behavior from the current dispatcher. If batching differs, compare resolved per-element/document Material and Atlas plus sorted adjacency.
- Cover UXML/USS load/save, layout/scale, controls, query/event APIs, HTML outputs/diagnostics, rendering order/batch keys, package disable/unload, examples, and Play-mode write isolation under `Example/Tests/UIElements`.
