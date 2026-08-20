---
name: bengine-tiledmap
description: Develop or use BEngine's Tiled Map package. Use for Tilemap runtime editing, tile palettes, atlas slicing, tile rendering, painting tools, or code under src/TiledMap.
---

# BEngine Tiled Map

## Package boundary

- Use package ID `com.bengine.tiledmap`; it is disabled by default and depends only on Core.
- Keep runtime code in `BEngine.TiledMap` and authoring support in `BEngine.TiledMap.Editor`.
- Use `Vector2`, `Fix64`, scalar rotations and world sorting layers. Do not introduce 3D coordinates or compatibility APIs.

## Data and rendering

- Treat tile ID `0` as empty and positive IDs as palette entries.
- Preserve deterministic coordinate ordering and the versioned hidden cell string used by scene/Prefab serialization.
- Keep palettes in `*.tilepalette.yaml`; validate unique positive IDs and normalized Atlas UV rectangles.
- Accept a Core `*.atlas.yaml` as a palette source by importing its generated texture and named regions; do not repack or duplicate the source PNGs inside TiledMap.
- Submit tiles through `ISceneRenderContributor2D`. Preserve global Layer, Order, Hierarchy and Transparency ordering.
- Use the exact Material, Shader and Atlas identity as the batch key. Do not merge across a changed key or a non-adjacent sorted submission.

## Editor and validation

- Keep palette creation under `Assets/Create/2D`, Tilemap creation under `GameObject/2D Object`, and painting under `Window/2D`.
- Use Undo and mark the target component dirty for painting operations.
- Cover cell serialization, coordinate conversion, fill/swap/clear operations, palette validation, sort order and render batch keys when changing behavior.
