# Sprite and atlas authoring

## Build sprites and atlases

- A Sprite is the import mode of its PNG, backed by that image's `.meta` (`textureType=Sprite`, Pivot, PPU, Filter Mode, and Wrap Mode); it is not a separately authored asset. Legacy `*.sprite.yaml` files remain read-compatible only and must not be created for new content.
- Open `Window/2D/Texture Atlas`. New/Save/Reload control the manifest; set Max Size, Padding, and Extrude; select Sprite-mode image assets in Project and choose `Add Selected`; then Build.
- The Atlas Inspector and Texture Atlas window draw `Sources` as typed Sprite ObjectFields. Atlas manifests are versionless and persist every Sprite's stable `ownerGuid` plus `localIdentifier`; do not store source paths or a separate `SpriteReferences` list.
- The builder requires at least one valid Sprite-mode PNG input (or a legacy read-compatible source), unique references/names, deterministic power-of-two packing, and `Extrude <= Padding`. The generated PNG is an artifact-backed SubAsset identified by the Atlas GUID and a reserved localIdentifier; show it below the expanded Atlas node without exposing a second top-level Project asset.
- Assign only a `Sprite` to `SpriteRenderer.sprite`. `TextureAtlasResolver` finds any Atlas that references it and supplies UV, pivot, and batch identity; an unpacked Sprite uses its own texture. Batching requires adjacent sorted submissions with the same Material, Shader, and Atlas/texture identity.
