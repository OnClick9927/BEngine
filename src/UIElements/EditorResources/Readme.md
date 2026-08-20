# UIElements

Optional retained-mode user interface package for BEngine.

- Runtime: `BEngine.UIElements`, `UIDocument`, UXML, USS and GPU rendering.
- Editor: `BEngine.UIElements.Editor`, UI Builder and HTML conversion tools.
- TreeView: selection, foldout, inline rename, drag/drop reparenting, validation callbacks and GPU drop markers.
- Dependency direction: this package depends on `BEngine`; the engine and GPU IMGUI editor do not depend on it.

Disable `com.bengine.ui-elements` in the project package manifest to unload its editor assembly and exclude its runtime assembly and resources from builds.

Offline documentation is stored at `EditorResources/Doc/index.html` and opens from the package details page.

Import examples from `EditorResources/Examples` in Package Manager. `RuntimeHud.bpackage` introduces UIDocument,
UXML and USS; `ControlsGallery.bpackage` demonstrates common controls, events and runtime data binding.
