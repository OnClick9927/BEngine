---
name: bengine-ui-elements
description: "Develop and use BEngine's UIElements package retained-mode UI, including VisualElement trees, controls, layout, styling, UXML, USS, UIDocument, EditorWindow containers, GPU rendering, UI Builder, and HTML conversion. Use when changing code under Engine/src/Packages/UIElements/BEngine.UIElements or Engine/src/Packages/UIElements/BEngine.UIElements.Editor."
---

# BEngine UIElements

## Open the built-in tools and import examples

- Enable `com.bengine.ui-elements`; runtime types live in `BEngine.UIElements` and retained editor integration in `BEngine.UIElements.Editor`. `com.bengine.ui-builder` and `com.bengine.html-ui-converter` depend on this package.
- Import `Controls Gallery`. It installs to `Assets/Examples/ControlsGallery`; open `res/ControlsGallery.scene.yaml`, enter Play, and use Apply Preset/Reset to inspect UXML, USS, `Q<T>`, two-way `UIBinding`, BObservable value streams, and common controls. Use `Tools/Examples/UIElements/Open Controls Gallery in UI Builder` to edit the imported UXML.
- Import `Runtime HUD` separately. It installs to `Assets/Examples/RuntimeHud`; open `res/UIElements.scene.yaml`, enter Play, and click Advance Progress to update ProgressBar, TreeView, virtualized ListView, bubbled ClickEvent state, and a main-thread frame-scheduled heartbeat.
- Reimport preserves modified example files and repairs/updates the rest. Import/Reimport is blocked until the package is enabled and while Play mode disallows project writes.

## Create UI source files

- Use `Assets/Create/UI Toolkit/UI Document` in the top Assets menu or Project context menu to create a minimal `.bxml` text file. Use `Assets/Create/UI Toolkit/Style Sheet` to create `.bss`.
- `.bxml`, `.bss`, and `.bcss` are registered Project asset types backed by `VisualTreeAsset` and `StyleSheet`. The UI Builder package supplies an `[OnOpenAsset]` callback, so Project double-click opens `.bxml` in UI Builder.
- Open `Window/UIElement/UI Builder` to create or open a document. The Library exposes containers, controls, fields, collections, and toolbar elements with search; Hierarchy selects the retained tree; the central Viewport uses the window's `uiElementContainer`; Inspector edits identity, binding, classes, control values, layout, color, background, and border.
- The toolbar supports New/Open/Save, Undo/Redo, Duplicate, sibling reorder, Delete, and HTML Converter. The UIElement Viewport follows the active Game resolution and aspect ratio. New documents save by default to a generated `Assets/UI/New UI.bxml` path.
- Each UI Builder instance has an independent WindowId, Model, Controller, document history, selection, and panel scroll state. Use `EditorWindowRegistry` or the public `UIBuilderWindow.Open(path)` entry point for external integration.

## Convert HTML

- Open `Window/UIElement/HTML Converter`, or select an `.html`/`.htm` Project file and use `Assets/Convert HTML to UIElements`.
- Choose source HTML and destination UXML, then select whether to copy local resources and generate a C# binding. Convert writes `<name>.bxml`, `<name>.bss`, optional `<name>.generated.cs`, and `<name>.conversion.yaml` beside the destination.
- Inspect the conversion summary and every diagnostic. Unsupported/lossy HTML, CSS, scripts, remote resources, and unsupported tags must remain reported; the converter validates that the produced UXML loads and emits render commands but does not claim browser equivalence.
- Converter preferences remember only source/destination paths in EditorPrefs. `Edit/Preferences... > UIElement` shows built-in UI information and an Open UI Builder button; `Edit/Project Settings... > UIElement` is informational and exposes no editable runtime setting.

## Add and configure runtime UI

- UIElements has no GameObject creation command. Create/select a GameObject, then add `UI Toolkit/UI Document` through Inspector `Add Component` or the top Component AdvancedDropdown. It disallows duplicates.
- UIElements registers no `[DrawGizmo]` visualization or Scene handle. Select and configure `UIDocument` through Hierarchy/Inspector and validate its rendered result in Game view.
- Assign `visualTreeAsset` to an imported VisualTreeAsset, `sortingOrder`, one of the five UI `sortingLayer` values, Material, TextureAtlas reference, interactable state, scale mode, and reference width/height.
- `UIDocument` reloads on enable or when `visualTreeAsset` changes. An empty/missing source falls back to an empty Root and logs IO/data/access failures to Console.
- Build code trees from `VisualElement` and controls including `Label`, `Button`, `TextField`, `SearchField`, `FloatField`, `IntegerField`, `ColorField`, `Toggle`, `Slider`, `DropdownField`, `ProgressBar`, `Foldout`, `Image`, `Box`, `ScrollView`, `ListView`, `TreeView`, and Toolbar controls. Use `VisualTreeAsset.Load(...).Instantiate()`, `Q<T>`/`Query<T>`, class lists, event capture/bubbling, `bindingPath`/`UIBinding`, `VisualElement.schedule`, and `StyleSheet` parsing/application.
- UXML supports the engine's built-in control names and serialized properties. Structure belongs in UXML, style in USS, and behavior/event registration in C#.
- Each VisualElement may override UI sorting layer, order, Material, and Atlas; otherwise it inherits the UIDocument values. Runtime rendering adds document sorting order to element order and batches adjacent commands only when Material/Shader/Atlas identity matches.

## Run and debug UI

- Open Scene, Game, Inspector, and Console, then enter Play. Use Game resolution choices to test `ConstantPixelSize` and `ScaleWithScreenSize`; inspect Game Status for draw/batch counts and Console for UXML/resource failures.
- Open `Window/UIElement/Event Debugger` to capture UI event dispatches. Filter by UIDocument, event type, or target text; select an event to inspect callbacks, propagation paths, stopped flags, and public event properties. `UIEventDiagnostics.traceEmitted` is the public read-only trace stream and isolates observer failures from normal event delivery.
- Open `Window/UIElement/Samples` for interactive editor examples of button, scroller, toggle, radio, label, text, help, object, list, numeric, vector/color, and choice fields, including disabled and selected states.
- `UIDocument` routes pointer IDs independently, supports capture/release/cancel, text editing, sliders, scrolling and focus/tab navigation. Only the highest `sortingOrder` enabled/interactable document receives a pointer. Validate specialized collection interactions separately when adding new controls.
- Build accessibility metadata into controls through `accessibilityRole`, name, description, value, hidden/live-region flags, `focusable`, and `tabIndex`. Query `GetAccessibilitySnapshot`, dispatch supported actions with `PerformAccessibilityAction`, and connect an OS adapter through `UIAccessibility.bridge`. The package supplies the semantic/action bridge contract; do not claim a native screen-reader adapter on a platform until that adapter is implemented and device-tested.
- `PanelSettings` exists as a ScriptableObject API with scale mode/reference resolution, but UIDocument duplicates these fields and has no PanelSettings reference or asset creation command. Do not instruct users to create/assign a PanelSettings asset.
- Stop Play to discard runtime-created trees, values, selection, events, and UIDocument/component changes. Runtime UI cannot save UXML, USS, generated bindings, scene, prefab, Material, or Atlas assets.

## Extend UIElements

- Keep retained-mode runtime types in the `BEngine.UIElements` namespace under `Packages/UIElements/BEngine.UIElements`, editor integration under `Packages/UIElements/BEngine.UIElements.Editor`, and UI Builder/HTML conversion in their respective packages. EditorWindow chrome remains GPU IMGUI while `UIElementsEditorWindow` and `EditorWindowUIElementContainer` host retained content in a configured rectangle.
- Register the runtime UI renderer through the actual `SceneRenderContributor2DRegistry` API, as `UIElementsRenderRegistration` does. There is no `SceneOverlayRendererRegistry`; do not use or document that nonexistent name.
- A code-only control derives `VisualElement` or an existing field/control. Register external UXML controls with `UxmlFactoryRegistry.Register<T>(xmlName, factory, serializeAttributes, serializeChildren)`, preferably using an XML namespace. Dispose the registration at the owner's end of life; collectible assembly unload removes it automatically. UXML, UI YAML, in-memory assets and clones preserve custom state. Registered controls appear in UI Builder's Project Library tab. Use the UxmlFactoryOptions overload to supply display name, category, default attributes or opt out with ShowInLibrary=false; disposal/unload refreshes the list.
- Extend UI Builder only in the editor assembly, keep tools grouped under `Window/UIElement/...`, apply Undo/dirty/write policy for project files, and keep runtime preview claims accurate.
- Extend HTML conversion through `HtmlConversionOptions`, diagnostics/report models, DOM/CSS mappings, and round-trip/runtime-load validation. Keep AngleSharp out of runtime.
- Keep UI YAML payloads as engine-internal DTOs and persist them through `YamlUtility`; `VisualTreeAsset` is the public `BAsset` boundary and playback assets are read-only. Store shaders below `Resources/Shaders/UIElements` for the supported backends and render only through RHI/resource resolver abstractions.
- Keep TreeView selection, expansion, rename, drag/drop and drop-marker code deterministic. Those APIs are usable programmatically even where runtime pointer dispatch is not yet wired.

## Validate and troubleshoot

- If menus/components are absent, verify the current `BEngine.Editor` and `BEngine` binaries were exported together. If UI is blank, verify `visualTreeAsset` reference, file existence, UXML element names, USS source paths, Camera UI layer mask, enabled/active document, color/size, Material shader, and Console errors.
- If input does nothing, first test a Button in the highest-order interactable document; do not expect other control pointer behavior from the current dispatcher. If batching differs, compare resolved per-element/document Material and Atlas plus sorted adjacency.
- Cover UXML/USS load/save, EditorWindow container positioning/input, UI Builder MVC multi-window behavior and rendering, layout/scale, controls, query/event/binding/scheduler APIs, HTML outputs/diagnostics, rendering order/batch keys, examples, and Play-mode write isolation. The focused builder regression is `Temp/Engine/Tests/UIBuilderWorkflow`.
