---
name: bengine-ui-elements
description: "Develop and use the optional BEngine UIElements retained-mode UI package, including VisualElement trees, controls, layout, styling, UXML, USS, UIDocument, GPU rendering, UI Builder, and HTML conversion. Use when changing code under src/UIElements or building runtime and editor retained-mode interfaces instead of core GPU IMGUI."
---

# BEngine UIElements

## Preserve the optional boundary

- Use package ID `com.bengine.ui-elements`; it is disabled by default and Core must not reference it.
- Put retained-mode runtime types in `BEngine.UIElements`; put UI Builder and HTML conversion in `BEngine.UIElements.Editor`.
- Keep core editor windows on GPU IMGUI unless the package explicitly owns the window or tool.
- Keep AngleSharp dependencies in the editor assembly; runtime UI must not depend on HTML parser packages.

## Build runtime UI

- Compose `VisualElement` trees with controls such as fields, buttons, lists, tree views, scroll views, toolbars, and menus.
- Define structure in UXML, appearance in USS, and behavior in C#; load assets through `VisualTreeAsset`, `StyleSheet`, and `UIDocument`.
- Use `PanelSettings` for runtime scale behavior and the package layout engine for retained geometry.
- Render through `UIElementsRenderer`, command lists, and resource resolver interfaces; do not call a backend directly.
- Keep backend shader files under `Resources/Shaders/UIElements` for Vulkan, Direct3D, OpenGL, and WebGPU.

## Extend editor tooling

- Use UI Builder for package-owned visual authoring and keep generated project files in the project workspace.
- Use `HtmlToUIElementsConverter` to emit `.uxml`, `.uss`, optional `.generated.cs`, and a YAML conversion report.
- Preserve conversion diagnostics for unsupported or lossy HTML/CSS/script behavior; do not silently claim equivalence.
- Keep tree selection, foldout, rename, drag/drop reparenting, validation callbacks, and GPU drop markers consistent when extending `TreeView`.

## Preserve lifecycle and serialization

- Register the runtime scene overlay through `SceneOverlayRendererRegistry` when the package loads, and remove package-owned effects when disabled.
- Serialize UI assets through package document converters rather than embedding concrete UI models in Core.
- Update `package.yaml` for external references or dependency changes and add focused tests under `Example/Tests` for layout, conversion, interaction, and rendering batches.
