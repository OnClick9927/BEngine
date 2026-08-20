# Physics2D

Deterministic fixed-point 2D physics with rigidbodies, colliders, queries, and collision callbacks.

- Runtime assembly: `BEngine.Physics2D`
- Editor assembly: `BEngine.Physics2D.Editor`
- Package ID: `com.bengine.physics2d`
- Editor icon: `EditorResources/Physics2D.png`

Use `Rigidbody2D` with `BoxCollider2D`, `CircleCollider2D`, `CapsuleCollider2D`, or `PolygonCollider2D`. Runtime queries are exposed by `Physics2D`; simulation state uses `Vector2`, scalar angles, `Fix64`, and power-of-two `ulong` layer masks.
