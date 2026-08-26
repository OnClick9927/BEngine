---
name: bengine-property-attributes
description: "Develop and use the optional BEngine Property Attributes package for conditional Inspector display, validation, labels, path pickers, dropdowns, numeric constraints, progress bars, and inline actions. Use when annotating serialized fields or extending attribute-driven GPU IMGUI drawers under src/Packages/PropertyAttributes."
---

# BEngine Property Attributes

## Enable the package and import examples

- Open `Window/Package Manager`, select `Property Attributes`, and press `Import`. Package ID `com.bengine.property-attributes` is disabled by default and depends only on Core.
- Import `Attributes Gallery` from `Examples`. It installs to `Assets/Examples/AttributesGallery`; open `res/AttributesGallery.scene.yaml`, select `Attributes Gallery Bootstrap` in Hierarchy, and exercise conditions, dropdowns, ranges, paths, validation, and the inline reset button in Inspector.
- Import `Inspector Attributes and Drawer` separately. It installs to `Assets/Examples/InspectorAttributesAndDrawer`; open `res/PropertyAttributes.scene.yaml`, select `Property Attributes Showcase`, and inspect `runtime/PercentAttribute.cs` plus `Editor/PercentDrawer.cs` for the complete project-defined attribute/drawer split.
- Examples are imported one at a time and Reimport preserves local modifications. Import is blocked until the package is enabled and while the editor is in Play mode.

## Apply supported attributes in Inspector

- This package has no asset creation command, custom asset preview, component, GameObject command, `Tools/...` menu, settings page, EditorWindow, or Scene Gizmo. Its user surface is the normal Inspector after attributes are added to serialized fields/properties in a project `BObject`, component, or asset type.
- Use `[ShowIf(member)]` / `[HideIf(member)]` and `[EnableIf(member)]` / `[DisableIf(member)]`. A field/property/parameterless method can provide the condition. Supplying an expected value compares against that value; otherwise bool, non-empty string, non-null, or another present value is treated as true.
- Use `[Title(title, subtitle)]`, `[InfoBox(message, type, visibleIf)]`, `[LabelText(label)]`, `[Indent(level)]`, and `[ReadOnly]` for the currently implemented layout/state behavior.
- Use `[Dropdown("A", "B")]` for fixed string/enum choices or a provider member returning `IEnumerable`; use `[ProgressBar(min, max, title)]`, `[MinMax(min, max)]` on `Vector2`, `[Password(mask)]` on string, Core `[Range]` on numeric fields, `[Clamp]`, and `[MaxValue]`.
- Use `[FilePath]`, `[FolderPath]`, and `[AssetPath(extension)]` on strings. Their browse button opens `EditorFileDialog`; relative paths are normalized against the project, while AssetPath begins in `Application.dataPath`.
- Use `[Required(message)]`, `[ValidateInput(method, message, type)]`, and `[InlineButton(method, label)]`. A validator is an instance bool method with zero parameters or one parameter compatible with the field value; an action is an instance void method with zero parameters or one compatible parameter.
- Attributes are ordered by `PropertyAttribute.order`, and the shared drawer falls back to `EditorGUI.DefaultPropertyField` for normal serialization behavior.

## Know the current gaps

- `PrefixAttribute`, `SuffixAttribute`, `HorizontalLineAttribute`, and `EnumFlagsAttribute` are declared in the runtime assembly but `ExtendedPropertyDrawer` currently has no drawing branch for them. Do not claim a visible prefix, suffix, line, or flags mask UI until implemented and tested.
- `Required` and `ValidateInput` messages are drawn after the field, but `GetPropertyHeight` currently does not reserve lines for them. Long/error states can overlap subsequent controls; treat this as an implementation gap, not supported polished behavior.
- Invalid condition/provider/member names fail closed: conditions are false, choice lists are empty, validators fail, and actions do nothing. Use exact member names and compatible signatures.
- There is no package-specific runtime behavior to debug in Game view; attribute effects are editor-only. A `MonoBehaviour` containing the fields still runs normally, and stopping Play restores its authored pre-Play state.

## Use the Inspector workflow

- Create a C# component, reference `BEngine.PropertyAttributes` from its runtime assembly definition, add the runtime `using`, and annotate serialized members. Add the component through its own `[AddComponentMenu]` path or `Scripts/TypeName`.
- Select the GameObject in Hierarchy and edit the annotated component in Inspector. Use Undo/Redo for edits, watch validation boxes and enabled/visible changes immediately, and use Console for inline action logging.
- Do authoring edits outside Play mode. Play mode displays a cloned component; inline actions and value changes made there are discarded on Stop and cannot be saved to scene/prefab assets.

## Add or extend an attribute

- Put the declarative attribute class in `BEngine.PropertyAttributes`, derive it from `ExtendedPropertyAttribute` (or Core `PropertyAttribute` for a standalone drawer), and keep immutable metadata free of editor references.
- For shared behavior, extend `ExtendedPropertyDrawer` in `BEngine.PropertyAttributes.Editor`; for a standalone project/package behavior, create a `PropertyDrawer` with `[CustomPropertyDrawer(typeof(MyAttribute), useForChildren: ...)]` in an Editor assembly.
- Implement both `OnGUI` and accurate `GetPropertyHeight`, including every conditional decorator/error line. Read/write only through `SerializedProperty`, preserve Undo, multi-object behavior, disabled state, and Core fallback drawing.
- Resolve condition, provider, validator, and action metadata through `EditorReflectionCache`; never scan reflection on every repaint. Support inherited target members and zero/one-argument signatures consistently with the cache.
- Route file/folder selection through `EditorFileDialog`, use project-relative paths where requested, and do not write assets while `EditorAssetWritePolicy.CanWrite` is false.
- Keep the runtime assembly independent of `BEngine.Editor` and UIElements. The editor assembly may reference runtime and `BEngine.Editor`; no package menu/window is needed merely to register a drawer.

## Validate and troubleshoot

- If attributes are ignored, verify the package is imported, the consuming assembly references `BEngine.PropertyAttributes`, the target member is serialized, and the editor assembly containing the drawer compiled. Check Console for type/load failures.
- If a condition or button does not work, verify exact case-sensitive member name, instance member, supported return type, zero/one parameter count, and parameter assignability to the current boxed value.
- Cover visible/hidden and enabled/disabled states, stacking/order, provider choices, paths, numeric mutation, Undo, read-only behavior, invalid members, accurate height, external custom drawers, package disable/unload, and Play-mode isolation under `Example/Tests/PropertyAttributes`.
