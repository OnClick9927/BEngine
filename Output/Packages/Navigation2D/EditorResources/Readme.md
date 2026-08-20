# Navigation2D

Deterministic 2D occupancy-grid baking, A-star pathfinding, agents, obstacles, links, and modifiers.

- Runtime assembly: `BEngine.Navigation2D`
- Editor assembly: `BEngine.Navigation2D.Editor`
- Package ID: `com.bengine.navigation2d`
- Runtime dependency: `com.bengine.physics2d`
- Editor icon: `EditorResources/Navigation2D.png`

Use `NavigationSurface2D` to bake X/Y walkability from 2D colliders and obstacles. Query through `Navigation2D`, then move objects with `NavigationAgent2D`. The package has no terrain, height, NavMesh, or 3D geometry dependency.
