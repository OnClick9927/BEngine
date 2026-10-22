---
name: bengine-core
description: "Develop and use BEngine core runtime, editor host, launcher, player, YAML document, lifecycle, and rendering APIs. Use when changing code under Engine/src/Core, adding a core BEngine or BEngine.Editor API, extending GPU IMGUI, serialization, assets, scenes, or the graphics abstraction, or deciding whether functionality belongs in core rather than an optional package."
---

# BEngine Core

## Open the editor and locate its tools

- Start the Hub with `Engine/Engine/BEngine.Hub.exe` and select a project whose authored content is below `Assets`.
- Package releases live in `Engine/Engine/Packages/<name>/<name>-<packageVersion>`. Declare `minimumEngineVersion` in package.yaml; Package Manager lists compatible releases and pins the explicitly installed version. Its top toolbar places search/refresh on the left and create/export icons on the right. Create/export forms use compact, anchored Pop EditorWindows, without a modal backdrop; preserve their input when a native progress dialog takes focus. Pointer input and repaint hover must not pass through a popup to obscured windows. `Create package...` asks for the root namespace and selects the minimum engine version from the current/installed engines. Assembly directories and asmdef names are exactly the root namespace and its `.Editor` counterpart, without appending the package name; use these names in package.yaml, source namespaces and the Editor-to-Runtime reference. Suggest `Project.<package name>` only while the namespace has not been customized. Templates belong only to the current project's `Assets/Packages`. `Export package...` uses a dropdown listing direct children of `Assets/Packages` containing package.yaml, previews the fixed destination `Packages-Exported/<name>/<name>-<packageVersion>` next to Assets, and validates/compiles the snapshot before export. No editable source or target directory is exposed; recheck the source boundary before writing and reject duplicate releases. See `../../Doc/PackageAuthoring.md` for details.
- Open the primary panels from `Window/General/Console`, `Window/General/Game`, `Window/General/Hierarchy`, `Window/General/Project`, `Window/General/Inspector`, and `Window/General/Scene`. Open `Window/Package Manager` for packages and examples.
- Use the Console for editor and log diagnostics; `Window/Codex` opens the built-in Codex surface.
- Use `Edit/Project Settings... > Tags and Layers` for render layers; the obsolete duplicate Sorting Layers window was removed. The same settings window also exposes `Asset Bundles`, `Graphics`, `Player`, and `Scripting`. Layer values are contiguous natural indices `1..63`; the five default built-in layers may be renamed and reordered but not deleted, while custom layers may be added, deleted, renamed, and reordered.
- Use `Edit/Preferences...` for language, scale, font, theme, asset refresh, meta-file visibility, and the external script editor. Preferences and Project Settings are normal layout-persistent EditorWindows: dock, float, and redock them like any other panel; repeated menu commands focus the existing instance. General exposes Editor Scale as a precise slider limited to `0.5` through `1.8`.
- Floating EditorWindows are independent native windows. Move them across monitors or drag them back over the main dock; local popups remain with their owner. Saved off-screen bounds recover to a current work area after monitor topology changes. Unfocused floats repaint at a reduced rate and minimized floats stop GPU submission.
- File operations are `File/New Scene`, `Open Scene...`, `Open Scene Additive...`, `Save Scene`, `Save All Scenes`, `Show Project in Explorer`, and `Exit`. Save commands are unavailable when the Play-mode write policy is active.
- Selection/edit operations are `Edit/Undo`, `Redo`, `Copy`, `Paste`, `Duplicate`, `Rename`, `Delete`, `Select All`, `Deselect All`, and `Frame Selected`; their validators follow the focused Project/Hierarchy/Scene context.
- Persist current and named layouts below each project's `Library/Editor/Layouts`, isolated from other projects. Migrate legacy `ProjectSettings/EditorLayout.yaml`/`Layouts` once, display the current session as `Layout`, and keep built-ins `Default`, `2 by 3`, `Tall`, and `Wide` immutable. Keep project-independent preferences and themes in `Engine/Engine/Preferences/BEgine-<version>` for the running engine version; Hub settings and history belong in `Engine/Engine/Preferences/Hub`.
- Open the toolbar Undo History icon for a detailed TreeView of group name, operation/object counts, object names, and scene impact. Selecting a row moves the Undo cursor directly to that state and must preserve normal Redo-branch truncation after a new edit.
- Open `Window/Analysis/Profiler` for CPU, Rendering, Memory, and registered external modules. Keep module rows 132 px high and vertically scrollable; expose Editor/Runtime method/call-stack CPU data, detailed render counters and deltas, and managed/process memory, fragmentation, allocation-rate, delta, and allocation-site data. Register extensions with `EditorProfilerModuleRegistry` and use `Profiler Modules` to control visibility.
- Match Unity shortcuts for implemented commands: Q/W/E/R/F tools; Ctrl/Cmd+N/O/S scene operations; Ctrl/Cmd+Z and Ctrl/Cmd+Shift+Z Undo/Redo; Ctrl/Cmd+P, Ctrl/Cmd+Shift+P, and Ctrl/Cmd+Alt+P Play/Pause/Step; Ctrl/Cmd+1..5 primary windows; Ctrl/Cmd+7 Profiler; Ctrl/Cmd+Shift+C Console; F2 rename and Delete delete. Never route text editing shortcuts to window commands while an input field owns focus.
- Manage a docked window from its tab menu. The Console tab menu exposes `View Editor Log` and `Reveal Logs Folder`.
- Import the required Core example from `Window/Package Manager > BEngine Core > Examples > Core Getting Started > Import`. It is installed below `Assets/Examples/CoreGettingStarted`; open `res/Core.scene.yaml`, enter Play mode, use the configured Horizontal/Vertical axes and Space, and inspect lifecycle/coroutine output in Console. The imported editor assembly also registers `Tools/Examples/BEngine Editor Window`.

## Work with Project assets

- Switch the Project window between One Column and Two Column from its window menu. Its left tree has independently scrolling Assets and Packages panes; drag their horizontal separator to resize the lower Packages pane. The saved editor layout persists this height and clamps both panes to usable bounds after a resize.
- In Two Column mode, keep each file/folder label horizontally centered on its thumbnail. Long labels must remain clipped with an end ellipsis inside the tile while the full name stays available from the tooltip; preserve this at every supported Editor Scale. Project labels omit only the final suffix: `Spark.png` renders as `Spark`, while `Showcase.atlas.yaml` renders as `Showcase.atlas`; never rename the physical file or asset path for display.
- Project and Hierarchy commit Selection only after MouseDown and MouseUp occur on the same TreeView row. Moving off the row or starting a drag cancels that click and preserves Selection, so a Project/Hierarchy BObject can be assigned to an unlocked Inspector ObjectField. Keep the drag cursor active during the gesture and apply ObjectField type and `allowSceneObjects` validation at the drop target.
- Text editors use the native keyboard repeat delay and rate for held `Backspace`, `Delete`, Left, Right, `Home`, and `End`. Apply the first key immediately, stop on KeyUp or focus loss, and do not synthesize a second repeat stream when the native backend already emits repeats.
- The Project context menu and top `Assets` menu use the same `MenuItem` registry, including entries contributed by project scripts and packages. `/` creates an AdvancedDropdown subtree.
- Create built-in content with `Assets/Create/Folder`, `C# Script`, `Scripting/ScriptableObject Script`, `Assembly Definition`, `Scene`, `Prefab`, `Shader`, `Text/Text File`, `Text/Markdown File`, `Data/JSON File`, and `Data/YAML File`.
- Create authored render assets with `Assets/Create/Rendering/Material` and `Assets/Create/2D/Texture Atlas`. To author a Sprite, select a PNG in Project, set Inspector `Texture Type` to `Sprite`, adjust Pivot/PPU if needed, then click `Apply`; do not use or document an `Assets/Create/2D/Sprite` workflow. The current runtime image decoder supports PNG only.
- Create a custom editor theme with `Edit/Preferences > Theme > New`. The clone is stored below `Engine/Engine/Preferences/BEgine-<version>/Themes`; GUISkin assets use `.guiskin.yaml` and derive from `BAsset` in `BEngine.Editor`.
- Use the Assets menu for common Project-selection commands. `Copy Full Path`, `Import New Asset...`, and Scene additive-open commands remain available from the Project context menu.
- Use `Assets/Import Package...` and `Export Package...` for `.bpackage` archives. Build player content from Build Settings after configuring `Project Settings/Asset Bundles`.
- Select a texture, font, shader, script, scene, prefab, material, sprite, atlas, or other `BAsset` to inspect its type-specific preview. Drag the Preview splitter to resize it and use the foldout to collapse it.
- All project asset types derive directly from the unified `BAsset`; do not introduce parallel asset base-type categories. The `BAsset` Inspector shows source identity and an `Import Settings` section whenever the asset has an `AssetImporter`. Texture import settings are `compressionFormat`, `filterMode`, `wrapMode`, `generateMipMaps`, `maxTextureSize`, and `pixelsPerUnit`; font settings are `defaultSize`, `includeKerning`, and `characterSet`; shader settings are `strictCompilation` and `optimizationLevel`. Use the Inspector's Apply or Revert buttons explicitly.
- Preserve the editor pipeline `Source -> AssetDatabase -> AssetImporter -> Artifact -> BAsset`. AssetDatabase owns GUID/path/hash/SubAsset identity and caches loaded BAssets; the Assets pane is built from those records rather than raw disk enumeration. Invalidate affected cache entries after import, refresh, move, or delete. `Document<TAsset>` is an internal persistence bridge only, not another asset hierarchy.
- Runtime loads BAssets from AssetBundles and may create GPU objects from them, but cannot save an asset. Play mode uses the same write prohibition. Sprite is a Texture-created `BObject` SubAsset, not a standalone BAsset.

## Compile project CG shaders correctly

- Treat `Assets/**/*.cg` and `Assets/**/*.shader` as HLSL-semantic, single-stage CG sources. They are not Unity ShaderLab or `CGPROGRAM` containers; do not wrap them in a ShaderLab document.
- Resolve vertex stages from `*.vert.cg`, `*.vertex.cg`, or `#pragma stage vertex`. Resolve fragment stages from `*.frag.cg`, `*.fragment.cg`, or `#pragma stage fragment`/`pixel`. Resolve compute stages from `*.comp.cg`, `*.compute.cg`, or `#pragma stage compute`.
- Require `#pragma stage vertex|fragment|pixel|compute` for `*.shader` and `*.cg` files without a recognized stage suffix. The default entry point is `main`; `#pragma entry EntryName` selects another non-empty entry.
- Strip only BEngine's `#pragma stage` and `#pragma entry` lines before compilation. Leave other preprocessor directives in the compiler input.
- Preserve the pipeline `Assets source -> stage/entry parsing -> shaderc HLSL -> Vulkan 1.0 SPIR-V -> content-addressed .spv -> current pointer`. Shaderc uses performance optimization, automatic locations, and HLSL IO mapping.
- Include the compiler schema, normalized project path, stage, entry point, and complete source in the build ID. Reuse an existing SPIR-V artifact for unchanged input.
- Perform compilation in background preparation, then atomically update `current` pointers and deleted pointers in the apply phase. Clear `DefaultShaderResources` only after compiled or deleted results are applied. Never publish a failed artifact.
- Isolate failures per shader and use `ShaderCompilation.log` for compiler diagnostics. A minimal fragment source is:

```hlsl
#pragma stage fragment
#pragma entry FragmentMain

float4 FragmentMain() : SV_Target
{
    return float4(0.15, 0.75, 0.65, 1.0);
}
```

## Create and identify SubAssets

- A SubAsset has no independent top-level identity. Identify it by its main asset GUID plus a non-zero `localIdentifier`; main assets use local ID 0. Serialized BAsset references use `guid:<main-guid>#subasset=<localIdentifier>`, so moving or renaming the main asset preserves the reference.
- Use `AssetDatabase.AddObjectToAsset(objectToAdd, mainAssetOrPath)` for an embedded SubAsset and `AssetDatabase.RemoveObjectFromAsset(objectToRemove)` to detach it. A SubAsset cannot own another SubAsset, and imported representations such as a Sprite owned by its TextureImporter cannot be removed manually.
- Use `AssetDatabase.LoadAllAssetsAtPath(path)` for the main asset plus all imported, embedded, and artifact-backed representations. Use `LoadAllAssetRepresentationsAtPath(path)` for the representations only, `IsMainAsset`/`IsSubAsset` to classify an object, and `TryGetGUIDAndLocalFileIdentifier` to retrieve its stable identity. Runtime code can use `BAsset.LoadSubAsset<T>(mainPath, localIdentifier)`.
- Show SubAssets as children of their owner in the Project TreeView. Expanding the owner exposes rows that can be selected or dragged, without creating independent top-level asset identities. Typed ObjectFields, Inspector UI, and enumeration APIs must resolve the same objects; use `EditorUtility.SetDirty` and the normal save path after editing embedded data.

## Author and apply editor GUI skins

- Open `Edit/Preferences > Theme`. The list always shows built-in `Light`, `Dark`, and `Classic`, followed by custom `.guiskin.yaml` assets. Built-ins are stable `GUISkin` objects that can be selected through their ObjectField and inspected; their style/state Foldouts remain interactive, while leaf fields are read-only and they cannot be deleted.
- Click a Theme row's ObjectField to set `Selection.activeObject` and show that Skin in Inspector. `New` clones the active Skin below `Engine/Engine/Preferences/BEgine-<version>/Themes`; edit its built-in style slots and `customStyles`, then use the Inspector Apply bar to persist it. `GUIStyleState.backgroundImage` is a Texture ObjectField.
- Click `Set` to apply a Skin to the complete editor. `Delete` is available only for custom assets; deleting the active custom Skin falls back to Dark. Never mutate or attempt to save an `isBuiltIn`/`isReadOnly` Skin.
- Each custom Skin has Light, Dark, and Classic one-click preset buttons. A `GUISkin` owns every standard and `EditorStyles` slot directly; there is no separate SkinColors or palette. `customStyles` is serialized and `FindStyle`/`GetStyle` lookup is case-insensitive; use names scoped as `PackageName/ControlName` to avoid collisions.
- A `GUIStyle` owns its normal, hover, active, focused, onNormal, onHover, onActive, onFocused, and disabled `GUIStyleState` values plus sizing, border width, font size, alignment, wrapping, rich-text, and stretch settings. Clone styles or skins before editing when the source must remain unchanged.
- Read the active Skin through `GUI.skin`. Use `EditorAppearance.SetSkin(skin)` for an editor-wide change because it synchronizes theme state, appearance notifications, and repaint; direct `GUI.skin` assignment is reserved for host internals and focused tests.
- `GUI`, `EditorGUI`, `GUILayout`, `EditorGUILayout`, and `EditorToolbar` drawing overloads accept `GUIStyle?`. Passing `null` resolves the semantic slot on the current Skin, such as `button`, `textField`, `popup`, `foldout`, or `toolbarButton`; passing a non-null style overrides it. Prefer `null` or the dynamic `EditorStyles` properties so extensions follow theme changes.
- Default Inspector drawing recursively exposes ordinary serializable objects as Foldouts. Child fields are indented in proportion to `SerializedProperty.depth`; arrays and lists are traversed, null values are safe, and cyclic references are truncated.

```csharp
var skin = AssetDatabase.LoadAssetAtPath<GUISkin>("Assets/Editor/Studio.guiskin.yaml");
if (skin is not null)
    EditorAppearance.SetSkin(skin);

GUI.Button(rect, new GUIContent("Run"), style: null);
var packageStyle = GUI.skin.FindStyle("MyPackage/AccentButton") ?? GUI.skin.button;
GUILayout.Button("Build", packageStyle);
```

## Author scenes and components

- Create empty hierarchy objects from the Hierarchy add button. The GameObject menu retains `Create Empty Parent`, `2D Object/Sprite`, `Effects/Particle System 2D`, and `Camera 2D`; the Sprite command adds `SpriteRenderer` and the other creation commands add their matching components.
- Use the Hierarchy context menu for active-state, Transform, and hierarchy-order commands. `GameObject/Frame Selected`, `Copy Hierarchy Path`, `Rename`, `Duplicate`, and `Delete` act on the current selection.
- Add components from the Inspector's `Add Component` AdvancedDropdown or the top `Component` menu. Core paths are `Rendering/Camera 2D`, `Rendering/Sprite Renderer`, and `Effects/Particle System 2D`; external `[AddComponentMenu]` components appear automatically.
- Configure `Camera2D` with orthographic `size`, `backgroundColor`, `isMain`, render `priority`, `clearMode`, an independent `cullingMask`, and normalized `viewportRect`. Build masks with `SortingLayer.ToMask`, `LayerMask.MaskForLayer`, or `LayerMask.GetMask`; never bitwise-combine the natural-number layer values themselves. Camera rendering is priority ordered; component context commands include `Align With View` and `Move To View`.
- Configure `SpriteRenderer` with a `Sprite` reference, size, pivot or Sprite pivot, color, flip X/Y, Material, sorting layer, order, and opacity. The renderer discovers Atlas membership from the Sprite asset; it has no Atlas field.
- Configure `ParticleSystem2D` duration/loop/play-on-awake, emission, lifetime, speed/direction, size/color/rotation, angular velocity, maximum count, legacy Atlas/region fields, Material, sorting layer, order, and opacity. It may use the built-in UI layer to sort among UI submissions; resolve that layer by its stable built-in identity because its current natural-number value changes when layers are reordered.
- Component header context actions are `Reset`, `Copy Component`, `Paste Component Values`, optional `Edit Script`, inherited custom `[ContextMenu]` entries, and `Remove Component`. Reset invokes `Component.OnReset`; `MonoBehaviour.OnReset` delegates to virtual `Reset()`.
- Use `Tools/Remove Missing Components` for missing scripts. GameObject Prefab commands include `Save As Prefab Asset`, `Save As Variant`, `Open Prefab`, `Overrides`, `Apply All`, `Revert All`, and `Unpack`. The Overrides popup supports individual property and component changes with Undo/Redo; hierarchy additions/removals use Apply All/Revert All. Prefab v3 preserves nested sources, multi-level Variant inheritance and stable instance IDs across scene reloads. Imported sources refresh open instances on the next editor update, including Prefab Stage; Play/compile/import defers refresh. Preserve live identities, external references and shared/cyclic values during refresh and Undo. See `../../Doc/PrefabAuthoring.md` for API details and Windows Player/content-update validation.
- Use the Hierarchy's leftmost eye and picking controls to hide an object from Scene view or disable Scene picking, including descendants. These are editor-only visibility states.

## Use Scene and Game views

- In Scene view, W/E/R select Move, Rotate, and Scale tools. Handles remain at the selected GameObject's aggregate visual center and use distinct move arrows, rotation ring, and scale squares.
- Scroll zooms and middle-button drag pans; Scene view itself does not rotate. Click a rendered object to select it in both Scene and Hierarchy; repeated clicks at the same point cycle overlapping visible, pickable objects.
- Toggle all Gizmos from the Scene toolbar. Its dropdown can enable `All`, `None`, or individual component types under `Components/...`, including types from loaded external assemblies.
- In Game view, choose Free Aspect, a built-in resolution group, or a custom resolution. `Add Custom Resolution...` and the custom delete submenu manage project choices. Expand `Status` for FPS/frame time, camera, visible submission, batch, draw, triangle, vertex, screen, and graphics completeness data.
- Press Play from the toolbar or `Edit/Play` (`Ctrl+P`); an open Game view receives focus. `Edit/Pause` and `Edit/Step` control debugging. Use Console filtering/collapse/error-pause and Game Status while running.
- Play mode runs a cloned scene session. Inspector, Hierarchy, Scene, and Game show runtime objects while playing; stopping discards the clone and immediately restores authored scene/object/component state. Runtime mutations must never be saved back to scene, prefab, component, or other asset files.
- `EditorAssetWritePolicy` blocks asset creation, import/reimport, example import, atlas build/save, and other authoring writes while entering, running, or exiting Play mode. Do not bypass it from editor-facing write APIs.

## Build sprites and atlases

- A Sprite is the import mode of its PNG, backed by that image's `.meta` (`textureType=Sprite`, Pivot, PPU, Filter Mode, and Wrap Mode); it is not a separately authored asset. Legacy `*.sprite.yaml` files remain read-compatible only and must not be created for new content.
- Open `Window/2D/Texture Atlas`. New/Save/Reload control the manifest; set Max Size, Padding, and Extrude; select Sprite-mode image assets in Project and choose `Add Selected`; then Build.
- The Atlas Inspector and Texture Atlas window draw `Sources` as typed Sprite ObjectFields. Atlas manifests are versionless and persist every Sprite's stable `ownerGuid` plus `localIdentifier`; do not store source paths or a separate `SpriteReferences` list.
- The builder requires at least one valid Sprite-mode PNG input (or a legacy read-compatible source), unique references/names, deterministic power-of-two packing, and `Extrude <= Padding`. The generated PNG is an artifact-backed SubAsset identified by the Atlas GUID and a reserved localIdentifier; show it below the expanded Atlas node without exposing a second top-level Project asset.
- Assign only a `Sprite` to `SpriteRenderer.sprite`. `TextureAtlasResolver` finds any Atlas that references it and supplies UV, pivot, and batch identity; an unpacked Sprite uses its own texture. Batching requires adjacent sorted submissions with the same Material, Shader, and Atlas/texture identity.

## Use main-thread async and reactive flows

- Use `BTask` or allocation-reduced `BValueTask` for Runtime async methods. They are frame-driven engine awaitables, not wrappers around .NET `Task` or worker-thread APIs.
- Use `BEngine.Reactive.BObservable<T>`, `Subject<T>`, `BehaviorSubject<T>`, and `ReactiveProperty<T>` for event streams and state. Compose them with `Select`, `Where`, `Take`, `Skip`, `DistinctUntilChanged`, `Merge`, `CombineLatest`, `Delay`, or `Debounce`.
- Use `Observable.EveryUpdate`, `IntervalFrame`, `Timer`, and `Interval` only through the engine scheduler. Editor update and Player runtime update own scheduler ticks; user code must not create a second ticking loop.
- Treat `OnErrorResume` as a recoverable notification and `OnCompleted(BResult)` as the terminal signal. Dispose subscriptions with `BCompositeDisposable`, `BDisposableBag`, `BSerialDisposable`, or `AddTo` when their owner leaves the scene or window lifecycle.
- Convert only through `Observable.FromBTask`, `FromBValueTask`, `FirstAsync`, and `ForEachAsync`; cancellation uses `BCancellationToken` and must release the upstream subscription immediately.

## Use and extend cross-platform networking

- Use `BEngine.Networking` for portable TCP, UDP, HTTP, and WebSocket work. It delegates protocols to .NET `TcpClient`, `UdpClient`, `HttpClient`, and `ClientWebSocket`; never add a handwritten HTTP or WebSocket parser.
- Pass a `CancellationToken` to every operation and set a finite `Timeout` when the workflow needs a deadline. For TCP, UDP, and WebSocket inspect `LastResult`, `Error`, and `Exception` after failure. For HTTP inspect `Result`, `Error`, `ResponseCode`, response headers, and `DownloadData`/`DownloadText`.
- Handle `TimedOut`, `Canceled`, `ConnectionError`, `ProtocolError`, and `DataProcessingError` distinctly. Dispose or asynchronously dispose every client and request; do not reuse a sent `HttpNetworkRequest` or a used WebSocket client.
- Use `TcpNetworkClient.ConnectAsync/SendAsync/ReceiveAsync/CloseAsync` for streams. Use a bound `UdpNetworkClient(IPEndPoint)` and either connected or endpoint-based `SendAsync`; `ReceiveAsync` returns a `NetworkDatagram` with sender endpoint.
- Use `HttpNetworkRequest.Get/Delete/Post/Put` for UnityWebRequest-style one-shot calls. Set headers before `SendAsync`. Inject a shared `HttpClient` or an `HttpMessageHandler` for platform policy and deterministic tests; an external HttpClient is not owned unless explicitly requested.
- Use `HttpNetworkRequest.SendAsync(responseHandler, token)` to stream large responses without filling `DownloadData`. Validate response status before consuming a body and enforce application size limits inside the handler.
- Use `WebSocketNetworkClient.SendTextAsync`, `SendBinaryAsync`, bounded `ReceiveAsync(maximumMessageSize)`, and `CloseAsync`. Configure a `ClientWebSocketTransport.Options` instance before passing it to the client when headers, proxy, keep-alive, subprotocol, or certificates require platform configuration.
- Implement `IWebSocketTransport` for a test double or a genuinely different platform adapter. Prefer composition around the sealed TCP/UDP/HTTP clients; keep TLS, proxy, compression, handshake, framing, and protocol parsing in proven .NET implementations.

```csharp
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
using var request = HttpNetworkRequest.Get("https://example.test/status");
request.Timeout = TimeSpan.FromSeconds(5);
request.SetRequestHeader("Accept", "application/json");
await request.SendAsync(stop.Token);
if (request.Result == NetworkRequestResult.Success)
    Console.WriteLine(request.DownloadText);
```

## Preserve AssetBundle indexing and runtime updates

- Treat runtime catalogs as immutable snapshots. `AssetBundleCatalogIndex` clones a publicly supplied catalog and freezes address, bundle, per-bundle asset, and precomputed dependency-closure indexes. Use `ContainsAddress` and `GetDependencyClosure` for public inspection.
- Construct one index when each manager runtime state is created. Keep `LoadBytesAsync` and `LoadTextAsync` on dictionary lookups; do not reintroduce per-load `FirstOrDefault`, `ToDictionary`, asset `Where` scans, or dependency graph traversal.
- Support concurrent `InitializeAsync` and concurrent loads on one manager. Share one loaded-bundle task per content hash, protect cache creation/removal, reference counts, ZIP access, asset cache, and disposal against races, and release every `AssetBundleHandle<T>`.
- Serialize state-changing `InitializeAsync`, `ApplyUpdateAsync`, `RollbackAsync`, and `CleanupAsync` operations. Publish a fully constructed state atomically. A load captures one state snapshot and must remain valid while a newer state is published; concurrent application of the same plan performs one activation.
- Keep remote version/catalog checks, maximum sizes, SHA-256 verification, content-addressed object names, path traversal rejection, dependency validation, and active/previous rollback pointers.
- Stream bundle downloads through `HttpNetworkRequest.SendAsync(responseHandler, token)` with response-headers-first completion. Write into staging, incrementally hash and enforce declared/maximum sizes, flush, then move only a verified file into the object cache. Do not buffer a complete bundle in memory.
- Atomically persist catalogs and active/previous pointers through temporary files. On download or verification failure, remove `.part` files and leave the previous active state intact. Rollback switches verified pointers and must not download.
- Runtime is read-only: never save back to `Assets`, mutate a manager by editing `ActiveCatalog`, edit the index, or modify installed bundle content. Publish new content only through build, check, download, verify, and atomic apply.

```csharp
await using var bundles = new AssetBundleManager(new AssetBundleRuntimeOptions
{
    PackageName = "com.example.game",
    CacheDirectory = cachePath,
    BuiltInDirectory = builtInPath,
    RemoteBaseUri = new Uri("https://cdn.example.test/content/"),
    RequireHttps = true
});
await bundles.InitializeAsync(cancellationToken);
var plan = await bundles.CheckForUpdatesAsync(cancellationToken);
if (plan.HasUpdates)
    await bundles.ApplyUpdateAsync(
        plan, progress: null, cancellationToken: cancellationToken);
await using var text =
    await bundles.LoadTextAsync("Assets/Data/config.json", cancellationToken);
```

## Preserve runtime architecture

- Put portable runtime APIs in `Engine/src/Core/BEngine`; keep them independent of `BEngine.Editor` and optional packages. Put editor-only APIs and GPU IMGUI in `Engine/src/Core/BEngine.Editor`.
- Keep project selection/history in `BEngine.Launcher`, and exported-app startup/package loading in `BEngine.Player`. Load runtime files from `Resources` and editor files from `Editor`; Core intentionally has no `package.yaml`.
- Derive engine objects from `BObject`; use `GameObject`, `Component`, `Behaviour`, `MonoBehaviour`, and `ScriptableObject` according to lifecycle needs. Use `Fix64`, `Vector2`, and scalar angles for runtime simulation.
- Serialize through `YamlUtility`, `Document`, and registered converters/validators. Persist main-asset references as stable `Assets/...` or `Packages/...` paths; persist SubAsset references as the main GUID plus localIdentifier, and preserve concrete `$type` sidecars for base-typed asset fields.
- Keep layer values as contiguous natural indices from `1` through `63`, not powers of two. Preserve the stable identities of the five default built-in layers: they may be renamed and reordered but not deleted; custom layers may be added, deleted, renamed, and reordered. Resolve the built-in UI layer by identity rather than a fixed number. Keep masks as a separate bit representation and create them through `SortingLayer.ToMask`, `LayerMask.MaskForLayer`, or `LayerMask.GetMask`. Rendering sorts by layer, order, hierarchy, transparency, then stable submission order.

## Extend the editor accurately

- Add a static method with `[MenuItem("Root/Submenu/Command", false, priority)]` and an optional same-path validator `[MenuItem(..., true)]`. `Assets`, `GameObject`, and `Component` contributions are shared by top menus and relevant context/AdvancedDropdown surfaces.
- Add `[AddComponentMenu("Group/Name", order)]` to a concrete `Component`; types without it appear as `Scripts/TypeName` for `MonoBehaviour` or as their type name. `[DisallowMultipleComponent]` disables duplicate addition.
- Add `[CreateAssetMenu(fileName = ..., menuName = ..., order = ...)]` to a concrete `BObject`. Register a stable suffix and loader with `AssetTypeRegistry.Register<TAsset>` when it is not the generic `.asset.yaml` format.
- Create an `EditorWindow`, expose it through a `Window/...` `[MenuItem]`, and use `GetWindow<T>`, `Show`, `ShowAuxWindow`, `ShowPopup`, or `ShowAsDropDown`. Implement `OnGUI` and only the lifecycle callbacks actually needed.
- Use `[CustomEditor(typeof(T), editorForChildClasses)]`, `[CustomPropertyDrawer(typeof(T), useForChildren)]`, and preview overrides `HasPreviewGUI`, `OnPreviewGUI`, and `GetInfoString`. Use `SerializedObject`, `SerializedProperty`, Undo, and `EditorUtility.SetDirty` for authored changes.
- Use `EditorGUI.ObjectField` or `EditorGUILayout.ObjectField` for `BObject` references. Both expose typed/generic and `SerializedProperty` overloads; pass `allowSceneObjects: true` only when scene-object references are valid for the field.
- Use `[SettingsProvider]` to return a `SettingsProvider` under `Preferences/...` with `SettingsScope.User` or `Project/...` with `SettingsScope.Project`.
- Add inherited component commands with `[ContextMenu("Group/Command")]` on an instance `void` method with no parameters. Add Scene drawings with `[DrawGizmo]`; package-rendered pickable geometry also registers a `ScenePickingProviderRegistry` provider.
- Register asset/editor icons from a package's `Editor` through inherited `[EditorIcon("relative/path.png")]`, `[EditorWindowIcon]`, or `EditorIconRegistry.Register`. Use `[OnOpenAsset]` for custom asset opening and an `AssetImporter` subclass for source settings.
- Extend runtime rendering with `SceneRenderContributor2DRegistry`, scene work with an `ISceneRuntimeSystem` registered through `RuntimeSystemRegistry` or an `IEngineServiceModule`/DI service, and YAML with `DocumentConversionRegistry` plus `DocumentValidationRegistry`. Registrations must be owned by the loaded assembly so package unload can remove them.
- Keep GPU IMGUI allocation- and reflection-light in repaint/update loops; route reflective member access through cached registries such as `TypeCache` and `EditorReflectionCache`.

## Validate and troubleshoot

- Use `Edit/Recompile Scripts` and Console to diagnose compile, package, asset, rendering, or editor failures. Open the current editor log or its folder from the Console tab menu.
- Treat restored tab selection and native window focus as separate states. Layout restoration may restore the logically focused EditorWindow, but never call GLFW/native `Focus` until the owning native window is initialized, live, and not closing; let the first rendered frame perform the real focus handoff.
- When the Hub launches an Editor, pass a unique startup token and wait for the Editor's first-frame `ready` status before closing the Hub. A managed startup failure reports `failed`; a native crash or early exit is detected from the process, and an honest timeout is also a failure. In every case the Hub owns one modal failure dialog and includes the actual diagnostic log path.
- A directly launched Editor owns its startup-failure dialog for catchable exceptions. Write project startup failures to the project's `Library/Editor/Logs/EditorBootstrap.log`; if the Editor cannot produce that log because of a native crash or timeout, the Hub writes `EditorStartup.log` in the same directory. Store the launch handshake in `Library/Editor/Startup`. Only failures without project context use global `Temp/Logs`. Never show a stale or guessed log path.
- When an asset does not appear, verify its suffix registration, `.meta`, stable project path, package enabled state, and then use `Assets/Reimport` or `Refresh`. When a component/menu/gizmo is absent, verify its runtime/editor assembly boundary and discovery attribute signature.
- Add focused tests under `Temp/Engine/Tests/<feature>` and validate the narrowest affected runtime, editor, launcher, or player assembly. Include a Play-mode write-isolation regression for any new authoring API.
