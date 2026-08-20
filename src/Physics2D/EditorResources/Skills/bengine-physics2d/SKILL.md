---
name: bengine-physics2d
description: Develop or use BEngine's deterministic 2D physics package. Use for Rigidbody2D, Collider2D, fixed-point simulation, collision callbacks, ray/circle/box queries, or code under src/Physics2D.
---

# BEngine Physics2D

## Package boundary

- Use package ID `com.bengine.physics2d`; it is disabled by default and depends only on Core.
- Keep runtime types in `BEngine.Physics2D` and authoring support in `BEngine.Physics2D.Editor`.
- Use only `Vector2`, scalar angles, `Fix64`, and 2D bounds. Do not add 3D compatibility types.

## Runtime model

- Use `PhysicsWorld2D` for fixed-step simulation and `Physics2D` for static queries.
- Use `Rigidbody2D` with box, circle, capsule, or polygon colliders.
- Expose contacts through `Collision2D`, `ContactPoint2D`, and callbacks such as `OnCollisionEnter2D`.
- Keep integration, pair ordering, A-star-independent queries, and callback ordering deterministic with stable IDs and fixed-point math.
- Layer filters use BEngine's power-of-two `ulong` layers.

## Validation

- Cover integration, trigger/collision enter-stay-exit, layer filters, raycasts, circle/box overlaps, casts, and deterministic repeat runs as relevant.
- Keep resources under package-level `Resources` or `EditorResources`, and update `package.yaml` when dependencies change.
