# Sprite Shape 2D Guide

## Authoring

A Sprite Shape asset stores ordered control points. Linear points produce straight segments. Free points expose independent incoming and outgoing tangent offsets; Mirrored points keep the two offsets opposite. Fixed tessellation uses `SamplesPerSegment`; adaptive tessellation uses `FlatnessTolerance` and `MaxSubdivisionDepth` for deterministic recursive cubic subdivision.

Select a GameObject with `SpriteShapeRenderer2D` to drag anchors and tangent handles in the Scene view. The Inspector can insert or remove points and rebuild collision geometry. Open shapes use `EdgeCollider2D`; closed shapes use `PolygonCollider2D`. Changing the asset at runtime refreshes the generated collider during `LateUpdate`.

## Rendering

The edge mesh is split wherever accumulated path length crosses `EdgeRepeatLength`, so every tile restarts its U coordinate at zero. Closed outlines, including authored holes, are triangulated through the Physics2D polygon geometry pipeline and use bounds-relative fill UVs. The generated `PolygonCollider2D` receives the same holes and refreshes after runtime edits. Edge and fill use separate texture, UV rectangle, tint, and render submissions.

`CornerRules` selects the first matching rule for each linear anchor by turn angle and inner/outer classification. Rules specify texture, UV, size, and tint; a quad oriented along the corner bisector renders after the edge. Rule data is editable in the asset Inspector and saved with the asset.

The asset Inspector's Boolean operand and operation controls create new Union/Intersection/Difference/Xor assets in the source asset folder. Curves are baked into polygons. Each disconnected island becomes a separate asset with its holes and a deep copy of the source styles/corner rules. `SpriteShapeAsset.Combine` exposes the same operation to tools and scripts; source assets are preserved.

`.spriteshape.yaml` version 1 stores fixed-point vectors, colors, UV rectangles, widths, and repeat length as raw integer values. Runtime loading uses the package asset codec, so scenes and AssetBundles use the same format.

## Limits

The tessellator rejects zero-area or non-triangulatable closed outlines. Corner sprites decorate linear anchors and do not trim adjacent edge strips. Boolean results preserve polygon geometry rather than editable cubic tangents. Collision follows the same sampled centerline or holed fill outline used by rendering.
