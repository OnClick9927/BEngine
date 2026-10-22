---
name: bengine-tiledmap
description: Develop or use BEngine's Tiled Map package. Use for Tilemap runtime editing, tile palettes, atlas slicing, tile rendering, painting tools, or code under Engine/src/Packages/TiledMap.
---

# BEngine Tiled Map

## Enable the package and import examples

- Open `Window/Package Manager`, select `Tiled Map`, and press `Import`. Package ID `com.bengine.tiledmap` is disabled by default and depends only on Core at runtime.
- Import `AtlasPalette` from `Examples`. It installs to `Assets/Examples/AtlasPalette`; open `res/AtlasPalette.scene.yaml`. To author its palette, open `Window/2D/Tile Palette`, enter `Assets/Examples/AtlasPalette/res/AtlasPalette.tilepalette.yaml` in Palette, press Load, assign an Atlas path and 2x2 slicing, then Save.
- Import `RuntimePainting` separately. It installs to `Assets/Examples/RuntimePainting`; open `res/RuntimePainting.scene.yaml` and enter Play. Its script demonstrates `BoxFill`, `SetTile`, and transform flags; the checker texture remains until a real Atlas/image is assigned.
- Reimport preserves locally modified files and repairs/updates the rest. Import/Reimport is disabled while the package is disabled or the editor is in Play mode.

## Create a palette and Tilemap

- Use `Assets/Create/2D/Tile Palette` in the top Assets menu or Project context menu. It creates `New Tile Palette.tilepalette.yaml` with one 1x1 entry. The file extension receives the Tile Palette display type/icon, but there is no custom palette Inspector or double-click open callback.
- Use `GameObject/2D Object/Tilemap` from the top menu or Hierarchy context. It creates a `Tilemap` plus required `TilemapRenderer`, parents it to the current context when applicable, registers Undo, and selects it.
- The package registers no `Tools/...` command; palette authoring belongs to `Window/2D/Tile Palette` and tilemap creation belongs to the Assets, GameObject, and Component paths documented here.
- Alternatively add `2D/Tilemap` and `2D/Tilemap Renderer` through Inspector `Add Component` or the Component AdvancedDropdown. `TilemapRenderer` requires a `Tilemap` and disallows duplicates.
- Configure `Tilemap.palette` with an imported TilePalette asset reference, `cellSize`, `cellGap`, and `tileAnchor`. Configure `TilemapRenderer.color`, BottomLeft/TopLeft/BottomRight/TopRight sort order, camera culling, Material, inherited sorting layer/order, and opacity.
- Select the Tilemap in Scene view to see its selected-only grid Gizmo. The package also registers Scene picking for rendered cells, so clicking a visible tile selects its owning GameObject; Hierarchy eye/picking state and Camera layer mask are respected.

## Use the Tile Palette window

- Open `Window/2D/Tile Palette`. Use New, Save, Reload, or type an absolute/project-relative `.tilepalette.yaml` path and press Load. The window does not automatically load the currently selected Project asset.
- Set palette Name and Atlas. Set Columns/Rows and press `Slice Atlas` to generate normalized rectangular tile definitions from one image. If Atlas ends with `.atlas.yaml`, `Import Texture Atlas` loads Core Atlas regions instead of repacking its PNG.
- Select a tile in the Tiles list, rename it, and edit its Collider flag. Tile IDs must remain unique positive integers; `0` is empty.
- Select a GameObject containing `Tilemap`. If needed press `Assign Current Palette`. Choose Paint, Erase, or Flood, choose transform flags, set grid Radius, and click cells in the window's coordinate grid. This version paints in the Tile Palette window, not directly by dragging over Scene view.
- Use Box Fill Min/Max coordinates and Fill for a rectangle, or Clear All. Painting, palette assignment, fill, and clear record Undo, mark the Tilemap dirty, and repaint Scene.
- Save palette edits explicitly. Save generates a path in the active Project folder for a new palette and imports it; Play mode write policy prevents saving authored palette data during runtime debugging.

## Edit and render at runtime

- Runtime cell APIs are `GetTile`, `GetCell`, `HasTile`, `SetTile`, `ClearTile`, `ClearAllTiles`, `BoxFill`, `FloodFill`, `SwapTile`, `GetBounds`, `GetTiles`, `CellToLocal`, `CellToWorld`, `WorldToCell`, `SetPaletteOverride`, `TryGetPalette`, and `RefreshAllTiles`.
- Cell transform flags support FlipX, FlipY, Rotate90, and Rotate180. Scene/prefab serialization stores a deterministic hidden cell string and revision; preserve it when changing the component.
- Rendering is submitted through `SceneRenderContributor2DRegistry`, obeys Camera culling mask/viewport, global layer/order/hierarchy/transparency order, and batches only adjacent tiles with identical Material/Shader/texture identity.
- Open Scene, Game, Inspector, and Console, then enter Play to exercise runtime painting. Pause/Step to inspect cell/revision state. Stop discards runtime-added/removed cells and restores the authored scene clone; runtime painting cannot save to the scene or palette asset.

## Know the current limits

- `TileDefinition.HasCollider` is consumed by TilemapCollider2D in the optional `com.bengine.tilemap-physics2d` bridge package. Tilemap alone does not depend on Physics2D.
- Rules match authored neighbor IDs and choose a visual output; Animation resolves time-based frames. Both are editable in the palette Inspector and leave collision metadata unchanged. `cellLayout` supports Rectangle, Isometric and axial HexagonPointTop/HexagonFlatTop. Shared polygon outlines drive rendering, Scene picking, grid gizmos and optional bridge collision. Hex FloodFill uses six edge neighbors. Tiled `.tmx/.tsx` import remains unsupported.
- The Tile Palette `Prefab Brush` field enables linked prefab Paint/Erase/Box Fill without a palette. Instances are Tilemap children with persistent TilemapPrefabInstance cell identity; repeat painting avoids duplicates, and strokes support Undo/Redo. Clear the field to resume tile painting. Prefab Flood/Pick/Move are unsupported; changing cell layout does not reposition already painted scene objects.
- Palette Atlas is a string path. A Core `.atlas.yaml` can populate region definitions, while a plain image plus Columns/Rows uses rectangular slicing. Validate paths and do not describe it as an ObjectField.

## Extend the package

- Keep runtime data/rendering in `BEngine.TiledMap` and authoring in `BEngine.TiledMap.Editor`. Use `Vector2`, scalar rotation, `Fix64`, world sorting layers, and stable deterministic coordinate ordering.
- New components use `[AddComponentMenu("2D/...")]`; asset commands use exact `Assets/Create/2D/...` `[MenuItem]`; hierarchy commands use `GameObject/2D Object/...`; authoring windows use `Window/2D/...`.
- Register new asset suffixes/loaders/icons with `AssetTypeRegistry` and `EditorIconRegistry`. A real `BAsset` palette migration must preserve `.tilepalette.yaml` compatibility and add a custom Inspector/open handler before changing the user workflow.
- Extend rendered geometry through `ISceneRenderContributor2D`/`SceneRenderContributor2DRegistry`. Register matching Scene hit testing with `ScenePickingProviderRegistry` and visual authoring with `[DrawGizmo]`.
- Every editor mutation uses Undo, `EditorUtility.SetDirty`, scene repaint, stable project paths, and `EditorAssetWritePolicy`. Never write palette or scene data in Play mode.

## Validate and troubleshoot

- If menus/components are absent, verify Tiled Map is imported and both assemblies compile. If no tiles render, verify selected Camera layer, enabled Tilemap/Renderer, valid `palette`, positive tile IDs, tile definitions, texture/Atlas paths, Material, color/opacity, and camera culling.
- If the palette window cannot paint, select the Tilemap GameObject, load/save a palette, select a positive tile ID, and assign the current palette. If Scene click misses, check Hierarchy visibility/picking and cell alpha.
- Cover palette validation/import/slicing, cell serialization, coordinate conversion, fill/flood/swap/clear, transform flags, sort directions, culling, batch keys, Scene picking/Gizmos, example import, and Play-mode isolation under `Temp/Engine/Tests/TiledMap`.
