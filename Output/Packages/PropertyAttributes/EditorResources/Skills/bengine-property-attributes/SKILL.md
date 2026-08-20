---
name: bengine-property-attributes
description: "Develop and use the optional BEngine Property Attributes package for conditional Inspector display, validation, labels, path pickers, dropdowns, numeric constraints, progress bars, and inline actions. Use when annotating serialized fields or extending attribute-driven GPU IMGUI drawers under src/PropertyAttributes."
---

# BEngine Property Attributes

## Respect the split

- Use package ID `com.bengine.property-attributes`; it is disabled by default and depends only on Core at runtime.
- Keep attribute declarations and their immutable metadata in `BEngine.PropertyAttributes` without editor references.
- Keep `ExtendedPropertyDrawer` and all GPU IMGUI behavior in `BEngine.PropertyAttributes.Editor`.
- This package uses core `SerializedObject`, `SerializedProperty`, and `PropertyDrawer`; do not add a UIElements dependency.

## Apply existing attributes

- Use show/hide and enable/disable attributes for member-driven conditions.
- Use clamp, min/max, progress, dropdown, flags, password, path, label, prefix/suffix, indent, title, line, required, validation, and inline-button attributes for their specific Inspector behavior.
- Point condition, provider, validator, and action names at members on the serialized target; keep signatures compatible with `EditorReflectionCache` helpers.
- Compose multiple attributes through `PropertyAttribute.order` and preserve deterministic draw/validation order.

## Add an attribute

- Add one runtime attribute type in the runtime assembly and keep it declarative.
- Extend the shared drawer only for behavior that needs editor rendering or interaction.
- Resolve metadata through `EditorReflectionCache`; do not perform uncached reflection during every Inspector repaint.
- Use `EditorGUI.DefaultPropertyField` as the fallback so base serialization behavior remains consistent.
- Keep file/folder/asset selection routed through `EditorFileDialog` and project-relative path rules.

## Validate changes

- Extend `Example/Tests/PropertyAttributes` with rendering, condition, mutation, and invalid-member coverage.
- Verify disabled/hidden states, stacked decorators, and validation height as well as the value written through `SerializedProperty`.
