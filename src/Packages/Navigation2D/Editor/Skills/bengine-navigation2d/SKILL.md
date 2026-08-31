---
name: bengine-navigation2d
description: Develop or use BEngine's deterministic 2D navigation package. Use for occupancy-grid baking, A-star paths, NavigationAgent2D, surfaces, obstacles, links, modifiers, or code under src/Packages/Navigation2D.
---

# BEngine Navigation2D

## Enable the package and import examples

- Open `Window/Package Manager`, select `2D Navigation`, and press `Import`. This enables `com.bengine.navigation2d` and its required `2D Physics` (`com.bengine.physics2d`) runtime dependency.
- In `Examples`, import `Navigation2D Surface and Agent`. It installs to `Assets/Examples/NavigationSurfaceAndAgent`; open `res/Navigation.scene.yaml`, enter Play, press Space to change destination, and inspect path state/remaining distance in Console.
- Import `Navigation2D Dynamic Rebuild` separately. It installs to `Assets/Examples/DynamicRebake`; open `res/DynamicRebake.scene.yaml`, enter Play, and press Space to move the obstacle, call `NavigationSurface2D.UpdateNavigation`, and restore the agent destination.
- Reimport preserves locally modified example files and repairs/updates the rest. Import/Reimport is unavailable until the package is enabled and is blocked during Play mode.

## Add and configure navigation components

- Navigation2D has no asset creation menu, Project asset/preview, `GameObject/...` or `Tools/...` command, or dedicated navigation window. Author it on GameObjects through Inspector `Add Component` or the top `Component` AdvancedDropdown.
- Add `Navigation 2D/Navigation Surface 2D`. Configure `agentTypeId`, collection mode, local center/size, an independent physics `layerMask`, geometry source, grid `cellSize`, clearance `agentRadius`, and `buildOnStart`. Layer values are natural indices `1..63`; create the mask with `SortingLayer.ToMask`, `LayerMask.MaskForLayer`, or `LayerMask.GetMask` rather than bitwise-combining layer indices.
- Open the Surface component header context menu and choose `Bake` to build its in-memory occupancy grid or `Clear` to remove it. These are inherited `[ContextMenu]` commands and participate in Undo/dirty handling. `hasData` reports whether the current runtime/editor object holds a grid.
- Add `Navigation 2D/Navigation Agent 2D`. Configure radius, speed, acceleration, angular speed, stopping distance, automatic flags, position/rotation updates, area mask, agent type, avoidance mode, velocity, destination, and stopped state. Runtime status includes desired velocity, path pending/status, remaining distance, and has-path.
- Add `Navigation 2D/Navigation Obstacle 2D`; choose Box or Circle and configure center, size/radius, carving, and stationary-carve flag.
- Add `Navigation 2D/Navigation Link 2D`; configure local start/end, width, cost modifier, direction, and area. Add `Navigation 2D/Navigation Modifier 2D` to block area 1, optionally only for one Surface agent type; with a Collider2D it uses collider bounds, otherwise a unit local box. Add `Navigation 2D/Navigation Modifier Volume 2D` for a center/size/area volume.
- Select a navigation object in Scene view to draw selected-only Gizmos: Surface/Modifier/Volume bounds, Obstacle shape, Link arrows/width, and Agent radius/destination. Toggle each type from `Scene > Gizmos > Components/...`. Agent, Surface, and Obstacle also receive package icons.

## Run and debug paths

- Bake a Surface before calling path APIs, or enable `buildOnStart` and enter Play. Use `Navigation2D.CalculatePath` with `NavigationPath2D`, `Navigation2D.SamplePosition` with `NavigationHit2D`, or Agent `SetDestination`, `SetPath`, `ResetPath`, and `Warp`.
- Open the imported scene from Project, select the Surface, Agent, Obstacle, or Link in Hierarchy, and keep Scene, Game, Inspector, and Console together. Scene shows selected geometry, Inspector exposes path status, and Console can report API results. Pause/Step to inspect deterministic grid movement.
- The bake currently samples the Surface volume on an X/Y grid, excludes enabled carving obstacles, area-1 modifier volumes/modifiers, and non-trigger Physics2D colliders allowed by `layerMask`, and prevents diagonal corner cutting. Links add directed or bidirectional graph edges.
- Play uses a cloned scene. Runtime Bake/Clear/UpdateNavigation, destination changes, moved obstacles, and agent state are discarded when Play stops and must not be written to the scene asset.

## Do not overstate current behavior

- `collectObjects` is exposed but the current baker always collects across the scene; it does not implement All/Volume/Children filtering.
- `Navigation2D.CalculatePath` and `SamplePosition` accept `areaMask`, but the current selection/path implementation does not apply it. Agent `agentTypeId` does not select a matching Surface.
- Agent `autoBraking`, `autoRepath`, `angularSpeed`, and `obstacleAvoidanceType` are configuration-only today. Movement directly rotates toward velocity when `updateRotation` is true.
- Obstacle `carveOnlyStationary`, Link `costModifier` and `area`, and Link `width` in pathfinding are not evaluated; width is visualized by its Gizmo only. Area behavior is currently the special blocked value `1` for modifiers/volumes.
- The current algorithm returns complete or invalid paths; do not promise partial-path generation, live avoidance, a persisted bake asset, or a NavMesh visualization window.

## Extend the package

- Keep runtime algorithms/components in `BEngine.Navigation2D` and authoring code in `BEngine.Navigation2D.Editor`. Keep the required Physics2D dependency in `package.yaml`; do not introduce 3D, terrain, X/Z, height, or NavMesh contracts.
- New navigation components use `[AddComponentMenu("Navigation 2D/...")]`, fixed-point fields, and stable YAML names. Register legacy type moves with `ComponentTypeMigrationRegistry`.
- Add component actions with inherited `[ContextMenu]` instance `void` methods. Add Scene visualization with `[DrawGizmo]` in the editor assembly and package icons through `EditorIconRegistry`; users can then toggle the external type in the Scene Gizmos dropdown.
- A future navigation authoring surface must be a real `EditorWindow` with a `Window/2D/...` `[MenuItem]`, use Undo/dirty state, honor `SceneVisibilityManager`, and never mutate assets while `EditorAssetWritePolicy.CanWrite` is false.
- Keep A-star tie breaking, surface enumeration, cell order, link enumeration, and agent stepping deterministic with `Fix64` and stable IDs. Cache discovery outside hot loops.

## Validate and troubleshoot

- If components are absent, verify both `2D Navigation` and `2D Physics` are imported and the package assemblies compiled. If Bake produces no path, verify Surface enabled/active, positive size/cell size, `hasData`, source/target inside or near walkable cells, and collider layers included by `layerMask`.
- If a Gizmo is absent, select the object, enable global Gizmos and that component type, and verify the component/GameObject is enabled and Scene-visible.
- Cover complete/invalid paths, blocked cells, diagonal prevention, links, modifiers, physics layer filters, dynamic rebuild, deterministic repetition, dependency enable/disable, and Play-mode isolation under `Example/Tests/Navigation2D`.
