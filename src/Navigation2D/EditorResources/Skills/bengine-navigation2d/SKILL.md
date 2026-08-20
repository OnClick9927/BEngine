---
name: bengine-navigation2d
description: Develop or use BEngine's deterministic 2D navigation package. Use for occupancy-grid baking, A-star paths, NavigationAgent2D, surfaces, obstacles, links, modifiers, or code under src/Navigation2D.
---

# BEngine Navigation2D

## Package boundary

- Use package ID `com.bengine.navigation2d`; it is disabled by default and depends on `com.bengine.physics2d`.
- Keep runtime algorithms and components in `BEngine.Navigation2D`; keep authoring support in `BEngine.Navigation2D.Editor`.
- Navigation is an X/Y occupancy grid. Do not introduce NavMesh, X/Z projection, terrain, height, or 3D geometry contracts.

## Runtime model

- Use `Navigation2D` for path and sampling operations and `NavigationPath2D`/`NavigationHit2D` for results.
- Use `NavigationSurface2D` to bake a grid from 2D colliders and obstacles.
- Use `NavigationAgent2D`, `NavigationObstacle2D`, `NavigationLink2D`, `NavigationModifier2D`, and `NavigationModifierVolume2D` for scene behavior.
- Keep grid collection, A-star tie-breaking, diagonal corner rules, and agent stepping deterministic with fixed-point types and stable IDs.

## Validation

- Cover complete/partial/invalid paths, blocked cells, diagonal corner prevention, links, modifiers, and deterministic repeatability as relevant.
- Verify disabling Navigation2D removes only its code/resources and leaves Physics2D usable.
