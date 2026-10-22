# Sprite Shape 2D

Sprite Shape 2D turns editable linear or cubic Bezier splines into textured 2D geometry. It supports fixed or adaptive tessellation, open and closed paths, edge textures repeated by path length, triangulated fills with holes, and synchronized EdgeCollider2D or PolygonCollider2D components.

Create a `.spriteshape.yaml` asset from **Assets > Create > 2D > Sprite Shape**, assign it to `SpriteShapeRenderer2D`, and select the object to edit anchors and tangents in the Scene view. Scene drags are grouped into one Undo operation and asset writes are blocked during Play mode.

See `Doc/SpriteShape2D.md` for runtime behavior and current limits.
