# Tiled Map

Sparse 2D tilemaps with YAML palettes, atlas slicing, deterministic runtime editing and batched rendering.

- Runtime assembly: `BEngine.TiledMap`
- Editor assembly: `BEngine.TiledMap.Editor`
- Package ID: `com.bengine.tiledmap`
- Palette asset: `*.tilepalette.yaml`

Create a Tilemap from `GameObject/2D Object/Tilemap`, create a palette from
`Assets/Create/2D/Tile Palette`, then paint it in `Window/2D/Tile Palette`.

`TilemapRenderer` uses the engine's shared layer, order, hierarchy and transparency sorting.
Adjacent tiles batch only when their Material, Shader and Atlas keys are identical.

Tile Palette accepts either a raw PNG for grid slicing or a Core `.atlas.yaml`. Enter the Texture Atlas path and choose `Import Texture Atlas` to create one tile per packed region while retaining matching collider and tint metadata.
