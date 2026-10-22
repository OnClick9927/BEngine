# Serialized properties and selection compatibility

`SerializedProperty.Next` and `NextVisible` traverse serialized fields, nested classes and structs,
array/List elements, and inline Vector/Color/Rect members. `enterChildren: false` skips the current
subtree. A cursor obtained through `FindProperty` or `Copy` can continue traversing without changing
the Inspector's cached property. `NextVisible` skips hidden fields; `Next` includes them. Traversal
stays within one BObject and stops descending at a repeated managed reference in the current ancestry.

BEngine does not expose Unity's synthetic string-character or `Array.size` child properties.
Use `stringValue` and `arraySize`; array element paths support both `items[0]` and
`items.Array.data[0]`. Readonly root members are not serialized and their children are not editable.
The immutable BEngine Vector/Color/Rect types expose read-only components during traversal;
assign their complete value through `vector2Value`, `vector4Value`, `colorValue` or `rectValue` to edit them.

`SerializedObject` stages edits until Apply. Update and Dispose discard pending edits. Apply writes
modified paths, with parent assignments absorbing later child edits. Editing an ordinary nested
field preserves its existing object identity and unrelated runtime state. Buffer snapshots read
fields rather than invoking unrelated user getters. Failing setters trigger rollback of every
attempted write; rollback errors retain the original failure. Undo postprocess callbacks are isolated
so one faulty extension does not leave a successful edit without Undo.

`Selection.GetFiltered` supports TopLevel, Deep, Editable, ExcludePrefab, Assets and DeepAssets,
including combined flags, generic/interface queries and the Type overload. Component queries return
the first matching component on each selected GameObject. TopLevel removes selected descendants;
Deep visits descendants once. Editable checks `HideFlags.NotEditable` on both the owner and component.
ExcludePrefab removes prefab assets while retaining scene prefab instances. Assets restricts results
to persistent objects; DeepAssets expands imported folders recursively through the active project's
asset database, including subfolders. BEngine also supports asset filters on non-GameObject queries.

BEngine represents a saved prefab as a `PrefabAsset`, not a persistent GameObject hierarchy.
Query `GetFiltered<PrefabAsset>` for Project prefab selections, or instantiate it to obtain GameObjects.
Selection does not materialize prefab contents or infer model-prefab/variant editability. DeepAssets
requires an attached project asset database and does not search arbitrary unimported files.

Behavior references:

- [Unity Selection reference source](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/Selection/Selection.cs)
- [Selection.GetFiltered](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Selection.GetFiltered.html)
- [SerializedProperty.Next](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedProperty.Next.html)
- [SerializedObject.Update](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SerializedObject.Update.html)
