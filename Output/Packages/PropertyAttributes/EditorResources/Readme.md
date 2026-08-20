# Property Attributes

Advanced Inspector metadata and GPU IMGUI property drawers for BEngine.

- Runtime assembly: `BEngine.PropertyAttributes`
- Editor assembly: `BEngine.PropertyAttributes.Editor`
- Package ID: `com.bengine.property-attributes`

The package provides conditional display, validation, labels, path fields, dropdowns, numeric constraints and
inline actions through `SerializedProperty` and `PropertyDrawer`. It does not require USS or UIElements resources.
The empty `Resources` and `EditorResources` directories are retained as package resource roots.

Offline documentation is stored at `EditorResources/Doc/index.html` and opens from the package details page.

Import examples from `EditorResources/Examples` in Package Manager. `InspectorAttributesAndDrawer.bpackage`
introduces attributes and custom drawers; `AttributesGallery.bpackage` provides a broader attribute gallery.
