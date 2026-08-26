---
name: bengine-physics2d
description: Develop or use BEngine's deterministic 2D physics package. Use for Rigidbody2D, Collider2D, fixed-point simulation, collision callbacks, ray/circle/box queries, or code under src/Packages/Physics2D.
---

# BEngine Physics2D

## Enable the package and import examples

- Open `Window/Package Manager`, select `2D Physics`, and press `Import`. Package ID `com.bengine.physics2d` is disabled by default and depends only on Core.
- Import `Rigidbody and Queries` from its `Examples` tab. It installs to `Assets/Examples/RigidbodyAndQueries`; open `res/Physics2D.scene.yaml`, enter Play, watch the body fall/bounce, press Space to reset/relaunch it, and inspect collision/raycast messages in Console.
- Import `Triggers and Shape Queries` separately. It installs to `Assets/Examples/TriggersAndQueries`; open `res/TriggersAndQueries.scene.yaml`, enter Play, watch the probe cross the trigger, press Space for immediate overlap/circle-cast queries, and inspect enter/stay/exit messages.
- Reimport preserves modified files and repairs or updates the remaining example installation. Import/Reimport is disabled while the package is disabled or Play mode prevents asset writes.

## Add and configure physics components

- Physics2D has no dedicated `GameObject/...` or `Tools/...` command, asset creation menu/preview, settings page, or physics window. Create/select GameObjects, then use Inspector `Add Component` or the top `Component` AdvancedDropdown.
- Add `Physics 2D/Rigidbody 2D`. Configure mass, gravity scale, linear/angular damping, Dynamic/Kinematic/Static body type, simulated state, velocities, position/rotation constraints, collision detection mode, and interpolation metadata.
- Add one or more `Physics 2D/Box Collider 2D`, `Circle Collider 2D`, `Capsule Collider 2D`, or `Polygon Collider 2D`. Every collider has trigger, local offset, optional `PhysicsMaterial2D`, attached Rigidbody, bounds, and closest-point APIs. Shapes add size, radius, capsule direction, or polygon point lists.
- `PhysicsMaterial2D` exposes fixed-point friction and bounciness, but it is currently a plain `BObject` with no `CreateAssetMenu`, suffix registration, or custom Inspector. Do not tell users there is an `Assets/Create/Physics Material 2D` command. Examples can construct and assign it from code.
- Rigidbody code can call `AddForce`, `AddTorque`, `AddForceAtPosition`, `MovePosition`, `MoveRotation`, `Sleep`, and `WakeUp` with `Force`, `Acceleration`, `Impulse`, or `VelocityChange`.
- Selecting a collider draws a green selected-only Scene Gizmo for its box, circle, capsule, or polygon shape. Toggle types through `Scene > Gizmos > Components/...`; Rigidbody and collider types receive the Physics2D icon.

## Query and debug the simulation

- Use static `Physics2D.Raycast`, `RaycastAll`, `OverlapCircle`, `CheckCircle`, `OverlapBox`, `CheckBox`, and `CircleCast`. Every query accepts the engine's power-of-two `ulong` layer mask and `QueryTriggerInteraction` where applicable.
- Global runtime settings are `Physics2D.gravity`, `queriesHitTriggers`, `autoSimulation`, and `velocityIterations`. Call `Simulate(step)` only with a positive fixed step and normally only when auto simulation is disabled; use `SyncTransforms`, `IgnoreCollision`, and `GetIgnoreCollision` as needed.
- Receive `OnCollisionEnter2D`, `Stay2D`, and `Exit2D` with `Collision2D`; receive trigger phases with the other `Collider2D`. Callbacks are discovered on enabled `MonoBehaviour` components on both GameObjects and exceptions are logged to Console.
- Open the imported scene from Project, select its Rigidbody, Collider, trigger, or query driver in Hierarchy, and keep Scene, Game, Inspector, and Console together. Enter Play, use Pause/Step for fixed-step inspection, watch Rigidbody velocities/sleeping state, visualize selected colliders, and log query hits/contact points.
- Stopping Play discards the runtime scene clone, forces, velocities, contacts, ignored pairs, global scene state, and component changes. Runtime debugging must not update a scene, prefab, component, or material asset.

## Do not overstate current behavior

- Narrow-phase collision, ray queries, and overlap tests currently operate on axis-aligned bounds derived from each shape. Capsule and polygon Gizmos show their authored outlines, but simulation contacts are AABB-based and do not account for rotated shape geometry.
- Bounciness participates in impulse resolution. `PhysicsMaterial2D.friction` is currently not used by the solver.
- `RigidbodyInterpolation` is exposed but is not applied. `velocityIterations` is stored but the current solver does not loop by that value. Continuous and ContinuousDynamic perform a bounds ray sweep; Discrete and ContinuousSpeculative use normal discrete integration.
- Do not promise joints, composite colliders, a collision matrix settings UI, broad-phase visualization, contact editing handles, or a physics debugger window; none is registered.

## Extend the package

- Keep runtime types in `BEngine.Physics2D` and editor-only authoring in `BEngine.Physics2D.Editor`. Use only `Vector2`, scalar rotations, `Fix64`, and 2D bounds.
- New components use `[AddComponentMenu("Physics 2D/...")]`; register legacy type names with `ComponentTypeMigrationRegistry` and editor icons in the editor assembly.
- Add selected/non-selected visualization with `[DrawGizmo]`. A new physics asset needs a real `BAsset`/`ScriptableObject`, `[CreateAssetMenu]`, suffix/loader registration through `AssetTypeRegistry`, serialization, an EditorResources icon, and Apply/Revert coverage.
- A future physics settings page must return a `[SettingsProvider]` under `Project/Packages/Physics2D`; a debugger must derive `EditorWindow` and register a concrete `Window/2D/...` menu. Do not invent those paths before implementing them.
- Keep integration, collider pair order, query results, and callback enter/stay/exit order deterministic with stable IDs and fixed-point math. Use cached runtime message metadata, not per-step reflection.

## Validate and troubleshoot

- If components are absent, confirm `2D Physics` is imported and both runtime/editor assemblies compile. If a collider is ignored, check GameObject layer versus query mask, enabled/active/simulated state, trigger policy, ignored pairs, and whether its AABB actually overlaps.
- If callbacks are absent, verify the exact method name and a compatible single argument, both objects still exist in the active scene, and the receiving `MonoBehaviour` is enabled. Use Console for callback exceptions.
- Cover integration, constraints, trigger/collision phases, layer filters, ignored pairs, rays, overlaps, casts, deterministic repeats, package disable/unload, Gizmo discovery, and Play-mode isolation under `Example/Tests/Physics2D`.
